using System.Net;
using Jellyfin.Plugin.MagicCollections.Sync;
using Xunit;

namespace MagicCollections.Tests;

public sealed class SonarrCollectionSyncTests
{
    private const string BaseUrl = "https://media.example.com:9443/sonarr";
    private const string SourceUrl = BaseUrl + "/family-shows";
    private const string Key = "example-sonarr-api-secret";
    private const string StatusEndpoint = BaseUrl + "/api/v3/system/status";
    private const string Status = """{"appName":"Sonarr","version":"4.0.20.3014"}""";
    private const string TagEndpoint = BaseUrl + "/api/v3/tag";
    private const string SeriesEndpoint = BaseUrl + "/api/v3/series";
    private const string Tags = """[{"id":12,"label":"family-shows"},{"id":3,"label":"other"}]""";

    [Fact]
    public async Task A_tag_syncs_whole_series_using_Tvdb_and_optional_Tmdb_and_Imdb_IDs()
    {
        using var fixture = CreateFixture();
        fixture.Responses.RecordBody(SeriesEndpoint, """
            [
              {"id":901,"tvdbId":101,"tags":[3,12],"seasons":[{"seasonNumber":1}]},
              {"id":902,"tvdbId":102,"tmdbId":202,"tags":[12]},
              {"id":903,"tvdbId":103,"tags":[3]},
              {"id":904,"tvdbId":0,"tmdbId":0,"imdbId":null,"tags":[12]},
              {"id":905,"tvdbId":105,"tmdbId":0,"imdbId":"tt0123456","tags":[12]}
            ]
            """);
        var tvdbMatch = fixture.AddMedia(SourceMediaType.Series, "Tvdb", "101");
        var tmdbMatch = fixture.AddMedia(SourceMediaType.Series, "Tmdb", "202");
        var imdbMatch = fixture.AddMedia(SourceMediaType.Series, "Imdb", "tt0123456");
        fixture.AddMedia(SourceMediaType.Series, "Tvdb", "103"); // Different tag
        fixture.AddMedia(SourceMediaType.Series, "Tvdb", "901"); // Sonarr resource ID
        fixture.AddMedia(SourceMediaType.Series, "Tvdb", "0"); // Unavailable provider ID
        fixture.AddMedia(SourceMediaType.Movie, "Imdb", "tt0123456");

        await fixture.Sync("https://MEDIA.example.com:9443/sonarr/%66amily-shows", SourceUrl);

        var collection = Assert.Single(fixture.Library.ManagedCollections);
        Assert.Equal("family-shows", collection.Name);
        Assert.Equal(["Magic Collection", SourceUrl, "Sonarr"], collection.Tags);
        Assert.Equal(new[] { tvdbMatch, tmdbMatch, imdbMatch }.Order(), collection.Members.Order());
        Assert.Equal([(StatusEndpoint, Key), (TagEndpoint, Key), (SeriesEndpoint, Key)], fixture.Requests);
    }

    [Theory]
    [InlineData("[3]")]
    [InlineData("null")]
    public async Task A_valid_tag_with_no_tagged_series_clears_existing_membership(string tags)
    {
        using var fixture = CreateFixture();
        var original = fixture.AddMedia(SourceMediaType.Series, "Tvdb", "101");
        await fixture.Sync(SourceUrl);
        var collection = Assert.Single(fixture.Library.ManagedCollections);
        Assert.Equal([original], collection.Members);
        fixture.Responses.RecordBody(SeriesEndpoint, $$"""[{"id":901,"tvdbId":101,"tags":{{tags}}}]""");

        await fixture.Sync(SourceUrl);

        var updated = Assert.Single(fixture.Library.ManagedCollections);
        Assert.Equal(collection.Id, updated.Id);
        Assert.Empty(updated.Members);
    }

    [Fact]
    public async Task A_missing_tag_leaves_existing_membership_unchanged_without_reading_series()
    {
        using var fixture = CreateFixture();
        var original = fixture.AddMedia(SourceMediaType.Series, "Tvdb", "101");
        await fixture.Sync(SourceUrl);
        var collection = Assert.Single(fixture.Library.ManagedCollections);
        fixture.Requests.Clear();
        fixture.Responses.RecordBody(TagEndpoint, """[{"id":12,"label":"other"}]""");

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(SourceUrl));

        Assert.Contains("Tag does not exist", error.Message);
        Assert.Equal([original], collection.Members);
        Assert.Equal([(StatusEndpoint, Key), (TagEndpoint, Key)], fixture.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task A_failed_series_read_leaves_membership_unchanged_without_echoing_request_details(HttpStatusCode status)
    {
        using var fixture = CreateFixture();
        var original = fixture.AddMedia(SourceMediaType.Series, "Tvdb", "101");
        await fixture.Sync(SourceUrl);
        var collection = Assert.Single(fixture.Library.ManagedCollections);
        fixture.Requests.Clear();
        fixture.Responses.Record(SeriesEndpoint, () => new(status)
        {
            Content = new StringContent(Key),
            Headers = { Location = new("https://elsewhere.example/" + Key) }
        });

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(SourceUrl));

        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal([original], collection.Members);
        Assert.Equal([(StatusEndpoint, Key), (TagEndpoint, Key), (SeriesEndpoint, Key)], fixture.Requests);
    }

    [Fact]
    public async Task An_incomplete_series_response_does_not_apply_partial_membership()
    {
        using var fixture = CreateFixture();
        var original = fixture.AddMedia(SourceMediaType.Series, "Tvdb", "101");
        fixture.AddMedia(SourceMediaType.Series, "Tvdb", "102");
        await fixture.Sync(SourceUrl);
        var collection = Assert.Single(fixture.Library.ManagedCollections);
        fixture.Responses.RecordBody(SeriesEndpoint, """
            [{"id":902,"tvdbId":102,"tags":[12]},{"id":901,"tvdbId":101}]
            """);

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(SourceUrl));

        Assert.Contains("Invalid or incomplete", error.Message);
        Assert.Equal([original], collection.Members);
    }

    [Fact]
    public async Task The_same_tag_on_two_configured_servers_has_separate_membership_and_keys()
    {
        const string otherBase = "https://media.example.com:9443/sonarr-secondary";
        const string otherSource = otherBase + "/family-shows";
        const string otherKey = "secondary-sonarr-key";
        using var fixture = CreateFixture();
        fixture.Configuration.ArrServers = [.. fixture.Configuration.ArrServers,
            new() { BaseUrl = otherBase, ApiKey = otherKey }];
        fixture.Responses.RecordBody(otherBase + "/api/v3/system/status", Status);
        fixture.Responses.RecordBody(otherBase + "/api/v3/tag", Tags);
        fixture.Responses.RecordBody(otherBase + "/api/v3/series", """[{"id":901,"tvdbId":102,"tags":[12]}]""");
        var firstMember = fixture.AddMedia(SourceMediaType.Series, "Tvdb", "101");
        var secondMember = fixture.AddMedia(SourceMediaType.Series, "Tvdb", "102");

        await fixture.Sync(SourceUrl, otherSource);

        Assert.Collection(fixture.Library.ManagedCollections, first =>
        {
            Assert.Contains(SourceUrl, first.Tags);
            Assert.Equal([firstMember], first.Members);
        }, second =>
        {
            Assert.Contains(otherSource, second.Tags);
            Assert.Equal([secondMember], second.Members);
        });
        Assert.Equal([
            (StatusEndpoint, Key), (TagEndpoint, Key), (SeriesEndpoint, Key),
            (otherBase + "/api/v3/system/status", otherKey), (otherBase + "/api/v3/tag", otherKey),
            (otherBase + "/api/v3/series", otherKey)
        ], fixture.Requests);
    }

    [Fact]
    public async Task Reassigning_a_base_to_Sonarr_keeps_Radarr_and_Sonarr_collections_separate()
    {
        const string tag = "family-shows";
        using var fixture = CreateFixture();
        var movie = fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "101");
        var series = fixture.AddMedia(SourceMediaType.Series, "Tvdb", "101");
        fixture.Responses.RecordBody(StatusEndpoint, """{"appName":"Radarr","version":"4.0.5.5981"}""");
        fixture.Responses.RecordBody(BaseUrl + "/api/v3/movie", """[{"tmdbId":101,"tags":[12]}]""");

        await fixture.Sync(SourceUrl);
        fixture.Responses.RecordBody(StatusEndpoint, Status);
        await fixture.Sync(SourceUrl);

        Assert.Collection(fixture.Library.ManagedCollections, radarr =>
        {
            Assert.Equal(tag, radarr.Name);
            Assert.Equal(["Magic Collection", SourceUrl, "Radarr"], radarr.Tags);
            Assert.Equal([movie], radarr.Members);
        }, sonarrCollection =>
        {
            Assert.Equal(tag, sonarrCollection.Name);
            Assert.Equal(["Magic Collection", SourceUrl, "Sonarr"], sonarrCollection.Tags);
            Assert.Equal([series], sonarrCollection.Members);
        });
        // Both collections now have the same URL tag; lookup must still select the active service.
        await fixture.Sync(SourceUrl);
        Assert.Equal(2, fixture.Library.ManagedCollections.Count);
    }

    [Fact]
    public async Task An_unconfigured_Sonarr_base_is_reported_without_sending_a_request()
    {
        using var fixture = CreateFixture();
        const string unknown = "https://media.example.com:9443/unconfigured/family-shows";
        var unsupported = new List<string>();

        await fixture.Engine.RunAsync([unknown], unsupported.Add, default);

        Assert.Equal([unknown], unsupported);
        Assert.Empty(fixture.Requests);
        Assert.Empty(fixture.Library.ManagedCollections);
    }

    private static ArrSyncFixture CreateFixture()
    {
        var fixture = new ArrSyncFixture(new()
        {
            ArrServers = [new() { BaseUrl = BaseUrl + "/", ApiKey = Key }]
        });
        fixture.Responses.RecordBody(StatusEndpoint, Status);
        fixture.Responses.RecordBody(TagEndpoint, Tags);
        fixture.Responses.RecordBody(SeriesEndpoint, """[{"id":901,"tvdbId":101,"tags":[12]}]""");
        return fixture;
    }
}

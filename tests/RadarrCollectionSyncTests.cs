using System.Net;
using Jellyfin.Plugin.MagicCollections.Sync;
using Xunit;

namespace MagicCollections.Tests;

public sealed class RadarrCollectionSyncTests
{
    private const string BaseUrl = "https://radarr.example.com:9443/Media/Radarr";
    private const string SourceUrl = BaseUrl + "/movie-night";
    private const string Key = "example-radarr-api-secret";
    private const string StatusEndpoint = BaseUrl + "/api/v3/system/status";
    private const string TagEndpoint = BaseUrl + "/api/v3/tag";
    private const string MovieEndpoint = BaseUrl + "/api/v3/movie";
    private const string Tags = """[{"id":12,"label":"movie-night"},{"id":3,"label":"other"}]""";
    private const string Movies = """
        [
          {"id":901,"tmdbId":101,"imdbId":"tt1020304","tags":[3,12]},
          {"id":902,"tmdbId":0,"imdbId":"tt5060708","tags":[12]},
          {"id":903,"tmdbId":103,"tags":[3]},
          {"id":904,"tmdbId":0,"imdbId":null,"tags":[12]}
        ]
        """;

    [Fact]
    public async Task One_tag_on_a_configured_base_path_syncs_movies_after_detecting_Radarr()
    {
        using var fixture = CreateFixture();
        fixture.Configuration.ArrServers =
        [
            new() { BaseUrl = "https://radarr.example.com:9443", ApiKey = "other-radarr-key" },
            .. fixture.Configuration.ArrServers,
            new() { BaseUrl = "https://radarr.example.com:9443/Media/Sonarr", ApiKey = "sonarr-key" }
        ];
        var tmdbMatch = fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "101");
        var imdbMatch = fixture.AddMedia(SourceMediaType.Movie, "Imdb", "tt5060708");
        fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "103");
        fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "901");
        fixture.AddMedia(SourceMediaType.Series, "Imdb", "tt1020304");

        await fixture.Sync("https://RADARR.EXAMPLE.COM:9443/Media/Radarr/%6dovie-night", SourceUrl);

        var collection = Assert.Single(fixture.Library.ManagedCollections);
        Assert.Equal("movie-night", collection.Name);
        Assert.Equal(["Magic Collection", SourceUrl, "Radarr"], collection.Tags);
        Assert.Equal([tmdbMatch, imdbMatch], collection.Members);
        Assert.Equal([(StatusEndpoint, Key), (TagEndpoint, Key), (MovieEndpoint, Key)], fixture.Requests);
    }

    [Fact]
    public async Task A_settings_save_during_sync_takes_effect_on_the_next_run()
    {
        using var fixture = CreateFixture();
        const string newKey = "updated-radarr-key";
        var first = fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "101");
        var second = fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "103");
        fixture.Responses.Record(TagEndpoint, () =>
        {
            fixture.Configuration = new()
            {
                ArrServers = [new() { BaseUrl = BaseUrl, ApiKey = newKey }]
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(Tags) };
        });

        await fixture.Sync(SourceUrl, BaseUrl + "/other");

        Assert.Equal([(StatusEndpoint, Key), (TagEndpoint, Key), (MovieEndpoint, Key), (TagEndpoint, Key), (MovieEndpoint, Key)],
            fixture.Requests);
        Assert.Collection(fixture.Library.ManagedCollections,
            collection => Assert.Equal([first], collection.Members),
            collection => Assert.Equal([first, second], collection.Members));

        fixture.Requests.Clear();
        await fixture.Sync(SourceUrl);

        Assert.Equal([(StatusEndpoint, newKey), (TagEndpoint, newKey), (MovieEndpoint, newKey)], fixture.Requests);
    }

    [Fact]
    public async Task A_missing_tag_fails_but_an_existing_tag_with_no_movies_can_clear_the_collection()
    {
        using var fixture = CreateFixture();
        var original = fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "101");
        await fixture.Sync(SourceUrl);
        var collection = Assert.Single(fixture.Library.ManagedCollections);
        fixture.Requests.Clear();

        fixture.Responses.RecordBody(TagEndpoint, "[]");
        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(SourceUrl));
        Assert.Contains("Tag does not exist", error.Message);
        Assert.Equal([original], collection.Members);
        Assert.Equal([(StatusEndpoint, Key), (TagEndpoint, Key)], fixture.Requests);

        fixture.Responses.RecordBody(TagEndpoint, Tags);
        fixture.Responses.RecordBody(MovieEndpoint, "[]");
        await fixture.Sync(SourceUrl);

        Assert.Equal(collection.Id, Assert.Single(fixture.Library.ManagedCollections).Id);
        Assert.Empty(collection.Members);
    }

    [Theory]
    [InlineData("""[{"id":12,"label":"movie-night"},{"id":"12","label":"other"}]""")]
    [InlineData("""[{"id":12,"label":"movie-night"},{"id":13,"label":"movie-night"}]""")]
    [InlineData("""[{"id":12,"label":"movie-night"},{"id":13,"label":"\uD800"}]""")]
    [InlineData("""[{"id":13,"id":12,"label":"movie-night"}]""")]
    [InlineData("""[{"id":12,"label":"other","label":"movie-night"}]""")]
    public async Task A_malformed_tag_list_cannot_establish_membership(string response)
    {
        using var fixture = CreateFixture();
        fixture.Responses.RecordBody(TagEndpoint, response);

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(SourceUrl));

        Assert.Contains("Invalid or incomplete", error.Message);
        Assert.Equal([(StatusEndpoint, Key), (TagEndpoint, Key)], fixture.Requests);
        Assert.Empty(fixture.Library.ManagedCollections);
    }

    [Theory]
    [InlineData("""{"response":"example-radarr-api-secret"}""")]
    [InlineData("""[{"id":901,"tmdbId":101}]""")]
    [InlineData("""[{"id":901,"tmdbId":101,"tags":[12,"example-radarr-api-secret"]}]""")]
    [InlineData("""[{"id":901,"tmdbId":101,"tags":[12],"ta\u0067s":[]}]""")]
    [InlineData("""[{"id":901,"tmdbId":101,"tmdbId":0,"tags":[12]}]""")]
    [InlineData("""[{"id":901,"tmdbId":0,"imdbId":"tt1020304","imdbId":null,"tags":[12]}]""")]
    [InlineData("""[{"id":901,"tags":[12]}]""")]
    [InlineData("""[{"id":901,"tmdbId":"101","tags":[12]}]""")]
    [InlineData("""[{"id":901,"tmdbId":101,"imdbId":"example-radarr-api-secret","tags":[12]}]""")]
    [InlineData("""[{"id":901,"tmdbId":101,"tags":[12]},""")]
    public async Task Malformed_or_incomplete_movies_do_not_change_the_collection_or_echo_the_body(string response)
    {
        using var fixture = CreateFixture();
        var original = fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "101");
        await fixture.Sync(SourceUrl);
        var collection = Assert.Single(fixture.Library.ManagedCollections);
        fixture.Responses.RecordBody(MovieEndpoint, response);

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(SourceUrl));

        Assert.Contains("Invalid or incomplete", error.Message);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal([original], collection.Members);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task A_non_successful_read_does_not_echo_the_body_or_redirect_location(HttpStatusCode status)
    {
        using var fixture = CreateFixture();
        fixture.Responses.Record(TagEndpoint, () => new(status)
        {
            Content = new StringContent(Key),
            Headers = { Location = new("https://elsewhere.example/" + Key) }
        });

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(SourceUrl));

        Assert.Contains($"HTTP {(int)status}", error.Message);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal([(StatusEndpoint, Key), (TagEndpoint, Key)], fixture.Requests);
        Assert.Empty(fixture.Library.ManagedCollections);
    }

    [Fact]
    public async Task Transport_failures_do_not_expose_request_details()
    {
        using var fixture = CreateFixture();
        fixture.Responses.Record(TagEndpoint, () => throw new HttpRequestException(Key));

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(SourceUrl));

        Assert.Contains("API request failed", error.Message);
        Assert.DoesNotContain(Key, error.ToString());
    }

    [Fact]
    public async Task An_unconfigured_instance_is_reported_without_contacting_it_or_a_configured_server()
    {
        using var fixture = CreateFixture();
        var unknown = "https://unconfigured.example.com/movie-night";
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
        fixture.Responses.RecordBody(StatusEndpoint, """{"appName":"Radarr","version":"7.0.3.10918"}""");
        fixture.Responses.RecordBody(TagEndpoint, Tags);
        fixture.Responses.RecordBody(MovieEndpoint, Movies);
        return fixture;
    }
}

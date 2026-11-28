using System.Net;
using Jellyfin.Plugin.MagicCollections.Sync;
using Xunit;

namespace MagicCollections.Tests;

public sealed class ArrServerDetectionTests
{
    private const string Base = "https://media.example.invalid/Library";
    private const string Status = Base + "/api/v3/system/status";
    private const string Tag = Base + "/api/v3/tag";
    private const string Source = Base + "/watched";
    private const string Key = "private-status-key";

    [Theory]
    [InlineData("Radarr", "4.0.5.5981", "Sonarr", "movie", "tmdbId", "Tmdb", SourceMediaType.Movie)]
    [InlineData("Sonarr", "4.0.0.748", "Radarr", "series", "tvdbId", "Tvdb", SourceMediaType.Series)]
    public async Task An_authenticated_status_identifies_the_service_and_keeps_the_saved_base(
        string appName, string version, string instanceName, string route, string field, string provider, SourceMediaType mediaType)
    {
        using var fixture = CreateFixture();
        var media = Base + "/api/v3/" + route;
        fixture.Responses.RecordBody(Status, $$"""
            {"appName":"{{appName}}","version":"{{version}}","instanceName":"{{instanceName}}",
             "urlBase":"https://other.example.invalid","apiKey":"not-the-configured-key"}
            """);
        fixture.Responses.RecordBody(media, $$"""[{"{{field}}":101,"tags":[12]}]""");
        var match = fixture.AddMedia(mediaType, provider, "101");

        await fixture.Sync(Source);

        var collection = Assert.Single(fixture.Library.ManagedCollections);
        Assert.Equal(["Magic Collection", Source, appName], collection.Tags);
        Assert.Equal([match], collection.Members);
        Assert.Equal([(Status, Key), (Tag, Key), (media, Key)], fixture.Requests);
    }

    [Theory]
    [InlineData("""{"appName":"private-status-key","version":"4.0.5"}""", "Unsupported application")]
    [InlineData("""{"appName":"radarr","version":"4.0.5"}""", "Unsupported application")]
    [InlineData("""{"appName":"Radarr","version":"4.0.4.5922"}""", "Radarr 4.0.5 or newer")]
    [InlineData("""{"appName":"Sonarr","version":"3.0.10.1567"}""", "Sonarr 4.0 or newer")]
    [InlineData("""{"instanceName":"Radarr","version":"3.0.0.4204"}""", "Radarr 4.0.5 or newer or Sonarr 4.0 or newer")]
    [InlineData("""{"AppName":"Radarr","version":"4.0.5"}""", "Invalid or incomplete system status")]
    [InlineData("""{"appName":null,"version":"4.0.5"}""", "Invalid or incomplete system status")]
    [InlineData("""{"appName":"\uD800","version":"4.0.5"}""", "Invalid or incomplete system status")]
    [InlineData("""{"appName":"Sonarr","app\u004eame":"Radarr","version":"4.0.5"}""", "Invalid or incomplete system status")]
    [InlineData("""{"\uD800":"secret","appName":"Radarr","version":"4.0.5"}""", "Invalid or incomplete system status")]
    [InlineData("""{"appName":"Radarr","version":"4.0.4","version":"4.0.5"}""", "Invalid or incomplete system status")]
    [InlineData("""{"appName":"Radarr","version":"private-status-key"}""", "Invalid or incomplete system status")]
    [InlineData("""{"appName":"Radarr","version":" 4.0.5 "}""", "Invalid or incomplete system status")]
    [InlineData("""{"appName":"Radarr"}""", "Invalid or incomplete system status")]
    [InlineData("""[]""", "Invalid or incomplete system status")]
    [InlineData("{\"appName\":\"Radarr\"", "Invalid or incomplete system status")]
    public async Task Unsupported_or_invalid_status_never_changes_membership_or_exposes_response_values(
        string response, string reason)
    {
        using var fixture = CreateFixture();
        var original = await PopulateRadarrCollection(fixture);
        fixture.Responses.RecordBody(Status, response);

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(Source));

        Assert.Contains(reason, error.Message);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal([(Status, Key)], fixture.Requests);
        Assert.Equal([original], Assert.Single(fixture.Library.ManagedCollections).Members);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.PartialContent)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_status_HTTP_failure_is_not_empty_membership_and_does_not_follow_a_redirect(HttpStatusCode status)
    {
        using var fixture = CreateFixture();
        var original = await PopulateRadarrCollection(fixture);
        fixture.Responses.Record(Status, () => new(status)
        {
            Content = new StringContent(Key),
            Headers = { Location = new("https://other.example.invalid/" + Key) }
        });

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(Source));

        Assert.Contains($"system status: API returned HTTP {(int)status}", error.Message);
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal([(Status, Key)], fixture.Requests);
        Assert.Equal([original], Assert.Single(fixture.Library.ManagedCollections).Members);
    }

    [Theory]
    [InlineData(false, "system status: API request failed")]
    [InlineData(true, "system status: API request timed out")]
    public async Task A_status_transport_failure_does_not_expose_transport_details(bool timeout, string reason)
    {
        using var fixture = CreateFixture();
        fixture.Responses.Record(Status, () => throw (timeout
            ? new TaskCanceledException(Key)
            : new HttpRequestException(Key)));

        var error = await Assert.ThrowsAsync<SourceListReadException>(() => fixture.Sync(Source));

        Assert.Contains(reason, error.Message);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal([(Status, Key)], fixture.Requests);
    }

    private static ArrSyncFixture CreateFixture()
    {
        var fixture = new ArrSyncFixture(new()
        {
            ArrServers = [new() { BaseUrl = Base, ApiKey = Key }]
        });
        fixture.Responses.RecordBody(Tag, """[{"id":12,"label":"watched"}]""");
        return fixture;
    }

    private static async Task<Guid> PopulateRadarrCollection(ArrSyncFixture fixture)
    {
        fixture.Responses.RecordBody(Status, """{"appName":"Radarr","version":"6.0.4.10291"}""");
        fixture.Responses.RecordBody(Base + "/api/v3/movie", """[{"tmdbId":101,"tags":[12]}]""");
        var original = fixture.AddMedia(SourceMediaType.Movie, "Tmdb", "101");
        await fixture.Sync(Source);
        fixture.Requests.Clear();
        return original;
    }
}

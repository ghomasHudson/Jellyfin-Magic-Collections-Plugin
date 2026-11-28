using Jellyfin.Plugin.MagicCollections.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MagicCollections.Tests;

public sealed class SourceListUrlControllerTests
{
    [Theory]
    [InlineData("https://mdblist.com/lists/snoak/latest-movies-digital-release", true)]
    [InlineData("https://letterboxd.com/example/list/movies/", true)]
    [InlineData("https://www.imdb.com/list/ls055592025/", false)]
    [InlineData("https://www.imdb.com/chart/top/", false)]
    [InlineData("https://www.imdb.com/chart/toptv/", false)]
    [InlineData("https://www.bfi.org.uk/lists/10-great-documentaries-that-play-like-thrillers", true)]
    [InlineData("https://popular-movies-data.stevenlu.com/movies.json", true)]
    [InlineData("https://trakt.tv/users/Justin/lists/OPEN-YOUR-EYES/?access_token=fixture-token#items", true)]
    [InlineData("https://app.trakt.tv/discover/trending?mode=show", true)]
    [InlineData("https://app.trakt.tv/discover/trending?mode=show&genres=comedy", false)]
    [InlineData("https://app.trakt.tv/discover/recommended?mode=movie&ratings=78-100&runtimes=80-500", true)]
    [InlineData("https://app.trakt.tv/discover/recommended?mode=media&ratings=78-100&runtimes=80-500&genres=action", true)]
    [InlineData("https://app.trakt.tv/lists/smart/view/hidden-gems-6ae39104c63e248a", true)]
    [InlineData("https://app.trakt.tv/users/me/lists/saved-list", false)]
    [InlineData("https://app.trakt.tv/users/me/watchlist", true)]
    [InlineData("https://trakt.tv/users/me/watchlist/", true)]
    [InlineData("https://app.trakt.tv/users/someone-else/watchlist", false)]
    [InlineData("https://app.trakt.tv/users/me/watchlist/movies", false)]
    [InlineData("jellyfin/mincriticRating=9", true)]
    [InlineData("jellyfin/MINCRITICRATING=100", true)]
    [InlineData("jellyfin/minCriticRating=101", false)]
    [InlineData("jellyfin/minCriticRating=NaN", false)]
    [InlineData("jellyfin/minCriticRating=9&limit=3&sortBy=Random&includeItemTypes=Movie&list_name=Movies%20of%20the%20Day&list_desc=3%20highly-rated%20movies", true)]
    [InlineData("jellyfin/tags=film%20noir&genres=Mystery&list_name=Film-Noir%20Mysteries", true)]
    [InlineData("jellyfin/person=Wes%20Anderson&includeItemTypes=Movie&list_name=Wes%20Anderson%20Collection", true)]
    [InlineData("jellyfin/searchTerm=Asterix&list_name=Asterix%20%26%20Obelix", true)]
    [InlineData("jellyfin/searchTerm=%EF%BF%BD", false)]
    [InlineData("jellyfin/studios=Marvel%20Studios,Marvel%20Entertainment&includeItemTypes=Movie", true)]
    [InlineData("jellyfin/years=2025&includeItemTypes=Movie&parentId=814be7ca-fba3-4f5a-9850-001540d06df3", true)]
    [InlineData("jellyfin/years=2025&minCommunityRating=7.5&hasOverview=true", true)]
    [InlineData("jellyfin/minPremiereDate=2025-01-02T01%3A00%3A00%2B01%3A00&maxPremiereDate=2025-12-31", true)]
    [InlineData("jellyfin/officialRatings=PG-13%7CTV-MA", true)]
    [InlineData("jellyfin/seriesStatus=Ended,continuing", true)]
    [InlineData("jellyfin/hasSubtitles=true&is3D=FALSE", true)]
    [InlineData("jellyfin/minCommunityRating=10.1", false)]
    [InlineData("jellyfin/hasOverview=yes", false)]
    [InlineData("jellyfin/seriesStatus=1", false)]
    [InlineData("jellyfin/seriesStatus=Ended,", false)]
    [InlineData("jellyfin/hasSubtitles=1", false)]
    [InlineData("jellyfin/is3D=yes", false)]
    [InlineData("jellyfin/minPremiereDate=2025-01-02T01:00:00Z", false)]
    [InlineData("jellyfin/genreIds=814be7ca-fba3-4f5a-9850-001540d06df3", false)]
    [InlineData("jellyfin/personIds=814be7ca-fba3-4f5a-9850-001540d06df3", false)]
    [InlineData("jellyfin/studioIds=814be7ca-fba3-4f5a-9850-001540d06df3", false)]
    [InlineData("jellyfin/", false)]
    [InlineData("jellyfin/Items?genres=Mystery", false)]
    [InlineData("jellyfin/genres=Mystery&Genres=Comedy", false)]
    [InlineData("jellyfin/genres=Mystery,", false)]
    [InlineData("jellyfin/genres=%XY", false)]
    [InlineData("jellyfin/genres=%C3", false)]
    [InlineData("jellyfin/genres=Mystery#fragment", false)]
    [InlineData("jellyfin/years=0", false)]
    [InlineData("jellyfin/includeItemTypes=Movie,Episode", false)]
    [InlineData("jellyfin/includeItemTypes=1", false)]
    [InlineData("jellyfin/parentId=00000000-0000-0000-0000-000000000000", false)]
    [InlineData("jellyfin/parentId=movies", false)]
    [InlineData("jellyfin/sortBy=SortName", false)]
    [InlineData("jellyfin/sortBy=Random,SortName", false)]
    [InlineData("jellyfin/sortBy=Random", false)]
    [InlineData("jellyfin/limit=0", false)]
    [InlineData("jellyfin/limit=1&sortOrder=Descending", false)]
    [InlineData("jellyfin/userId=814be7ca-fba3-4f5a-9850-001540d06df3", false)]
    [InlineData("jellyfin/minCommunityRating=7.5&fields=Overview", false)]
    [InlineData("jellyfin/hasOverview=true&isFavorite=true", false)]
    [InlineData("jellyfin/genres=Mystery&api_key=secret", false)]
    [InlineData("https://example.com/jellyfin/genres=Mystery", false)]
    [InlineData("https://letterboxd.com/film/the-matrix/", false)]
    [InlineData("https://example.com/list", false)]
    [InlineData("not a URL", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Validate_reports_whether_a_registered_handler_supports_the_URL(string? url, bool supported)
    {
        using var provider = PluginTestServices.BuildProvider();
        var controller = ActivatorUtilities.CreateInstance<SourceListUrlController>(provider);

        Assert.Equal(supported, controller.Validate(new(url)));
    }

    [Theory]
    [InlineData("https://media.example.com/Radarr/tag_name", true)]
    [InlineData("https://MEDIA.EXAMPLE.COM:443/Radarr/family%20night", true)]
    [InlineData("https://media.example.com/radarr/tag_name", false)]
    [InlineData("http://media.example.com/Radarr/tag_name", false)]
    [InlineData("https://media.example.com:9443/Radarr/tag_name", false)]
    [InlineData("https://media.example.com.evil.invalid/Radarr/tag_name", false)]
    [InlineData("https://media.example.com/Radarr", false)]
    [InlineData("https://media.example.com/Radarr/tag_name/", false)]
    [InlineData("https://media.example.com/Radarr/extra/tag_name", false)]
    [InlineData("https://media.example.com/Radarr/extra/../tag_name", false)]
    [InlineData("https://media.example.com/Radarr/extra/%2e%2e/tag_name", false)]
    [InlineData("https://media.example.com/Radarr//tag_name", false)]
    [InlineData("https://media.example.com/Radarr/%20", false)]
    [InlineData("https://media.example.com/Radarr/%0d", false)]
    [InlineData("https://user:key@media.example.com/Radarr/tag_name", false)]
    [InlineData("https://@media.example.com/Radarr/tag_name", false)]
    [InlineData("https://media.example.com/Radarr/tag_name?apikey=key", false)]
    [InlineData("https://media.example.com/Radarr/tag_name#", false)]
    public void Arr_source_must_identify_one_tag_at_a_configured_scheme_host_port_and_base_path(string url, bool supported)
    {
        using var provider = PluginTestServices.BuildProvider(new()
        {
            ArrServers =
            [
                new() { BaseUrl = "https://media.example.com/Radarr/", ApiKey = "example-key" },
                new() { BaseUrl = "https://media.example.com/Sonarr/", ApiKey = "other-key" }
            ]
        });
        var controller = ActivatorUtilities.CreateInstance<SourceListUrlController>(provider);

        Assert.Equal(supported, controller.Validate(new(url)));
    }

    [Fact]
    public void Arr_source_rejects_duplicate_normalized_bases()
    {
        var first = new ArrServerConfiguration { BaseUrl = "https://media.example.com/Radarr", ApiKey = "first-key" };
        var duplicate = new ArrServerConfiguration { BaseUrl = "https://MEDIA.example.com:443/Radarr/", ApiKey = "second-key" };
        using var provider = PluginTestServices.BuildProvider(new()
        {
            ArrServers = [first, duplicate]
        });
        var controller = ActivatorUtilities.CreateInstance<SourceListUrlController>(provider);

        Assert.False(controller.Validate(new("https://media.example.com/Radarr/tag_name")));
    }

    [Theory]
    [InlineData("https://admin:password@media.example.com/Radarr", "example-key")]
    [InlineData("https://media.example.com/Radarr?apikey=example-key", "example-key")]
    [InlineData("https://media.example.com/Radarr#example-key", "example-key")]
    [InlineData("https://media.example.com/Radarr", "")]
    [InlineData("https://media.example.com/Radarr", "invalid\r\nkey")]
    public void A_base_or_key_that_cannot_be_used_safely_does_not_enable_a_saved_source(string baseUrl, string key)
    {
        using var provider = PluginTestServices.BuildProvider(new()
        {
            ArrServers = [new() { BaseUrl = baseUrl, ApiKey = key }]
        });
        var controller = ActivatorUtilities.CreateInstance<SourceListUrlController>(provider);

        Assert.False(controller.Validate(new("https://media.example.com/Radarr/tag_name")));
    }

    [Fact]
    public void Draft_URL_checks_only_need_bases_and_do_not_fall_back_to_removed_saved_pairs()
    {
        const string old = "https://old.example.invalid";
        const string draft = "https://new.example.invalid/proxy";
        using var provider = PluginTestServices.BuildProvider(new()
        {
            ArrServers = [new() { BaseUrl = old, ApiKey = "saved-key" }]
        });
        var controller = ActivatorUtilities.CreateInstance<SourceListUrlController>(provider);

        Assert.True(controller.Validate(new(old + "/tag_name")));
        Assert.False(controller.Validate(new(old + "/tag_name", [])));
        Assert.False(controller.Validate(new(old + "/tag_name", [draft])));
        Assert.True(controller.Validate(new(draft + "/tag_name", [draft])));
        Assert.False(controller.Validate(new(draft + "/extra/tag_name", [draft])));
        Assert.False(controller.Validate(new(draft + "/tag_name", [draft, draft + "/"])));
        Assert.True(controller.Validate(new("https://letterboxd.com/example/list/movies/", [draft])));
        Assert.True(controller.Validate(new("jellyfin/genres=Mystery&years=2025", [draft])));
        Assert.False(controller.Validate(new("jellyfin/sortBy=Random", [draft])));
    }

    [Fact]
    public void A_scoped_IPv6_address_does_not_match_an_arr_server()
    {
        const string unscoped = "http://[fe80::1]:7878/radarr";
        const string scoped = "http://[fe80::1%25eth0]:7878/radarr";
        using var provider = PluginTestServices.BuildProvider(new()
        {
            ArrServers = [new() { BaseUrl = unscoped, ApiKey = "saved-key" }]
        });
        var controller = ActivatorUtilities.CreateInstance<SourceListUrlController>(provider);

        Assert.True(controller.Validate(new(unscoped + "/family")));
        Assert.False(controller.Validate(new(scoped + "/family")));
        Assert.False(controller.Validate(new(unscoped + "/family", [scoped])));
    }

    [Fact]
    public void Draft_validation_rejects_decorated_sources_even_without_saved_credentials()
    {
        const string draft = "https://media.example.com/proxy";
        using var provider = PluginTestServices.BuildProvider();
        var controller = ActivatorUtilities.CreateInstance<SourceListUrlController>(provider);

        Assert.True(controller.Validate(new(draft + "/family-shows", [draft])));
        Assert.False(controller.Validate(new(draft + "/family-shows?apikey=key", [draft])));
        Assert.False(controller.Validate(new(draft + "/family-shows#key", [draft])));
    }
}

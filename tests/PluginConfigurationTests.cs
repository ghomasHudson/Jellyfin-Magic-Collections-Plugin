using System.Text.Json;
using System.Xml.Serialization;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.AutoCollections;
using Jellyfin.Plugin.MagicCollections;
using Jellyfin.Plugin.MagicCollections.Configuration;
using Jellyfin.Plugin.MagicCollections.Sync;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MagicCollections.Tests;

public sealed class PluginConfigurationTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("magic-collections-config-");

    [Theory]
    [InlineData("example-trakt-client-id")]
    [InlineData("")]
    public void Saving_settings_persists_clean_Trakt_URLs_separately_from_the_Client_ID_override(string clientId)
    {
        const string publicList = "https://app.trakt.tv/users/justin/lists/open-your-eyes";
        const string unrelated = "https://letterboxd.com/example/list/movies/?sort=rank#list";
        const string filteredChart = "https://app.trakt.tv/discover/trending?mode=show&genres=comedy";
        const string recommended = "https://app.trakt.tv/discover/recommended?mode=media&ratings=78-100&runtimes=80-500&genres=action";
        const string smartList = "https://app.trakt.tv/lists/smart/view/hidden-gems-6ae39104c63e248a";
        using var update = JsonDocument.Parse($$"""
            {
              "SourceUrls": [
                "{{unrelated}}",
                "http://trakt.tv/users/JUSTIN/lists/OPEN-YOUR-EYES/?access_token=query-token#fragment-token",
                "https://trakt.tv/shows/popular#fragment-token",
                "https://trakt.tv/users/me/watchlist/?access_token=query-token#fragment-token",
                "{{filteredChart}}",
                "http://app.trakt.tv/discover/recommended/?genres=action&runtimes=80-500&mode=media&ratings=78-100&access_token=query-token#fragment-token",
                "{{smartList}}/?access_token=query-token#fragment-token"
              ],
              "TraktClientId": "{{clientId}}",
              "PosterIcon": false
            }
            """);
        var plugin = CreatePlugin();
        IHasPluginConfiguration configuredPlugin = plugin;
        var config = Assert.IsType<PluginConfiguration>(update.Deserialize(configuredPlugin.ConfigurationType, JsonDefaults.Options));
        configuredPlugin.UpdateConfiguration(config);

        var expectedUrls = new[] { unrelated, publicList, "https://app.trakt.tv/discover/popular?mode=show",
            "https://app.trakt.tv/users/me/watchlist", filteredChart, recommended, smartList };
        Assert.Equal(expectedUrls, plugin.Configuration.SourceUrls);
        var xml = File.ReadAllText(plugin.ConfigurationFilePath);
        Assert.DoesNotContain("query-token", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("fragment-token", xml, StringComparison.Ordinal);
        var settings = JsonSerializer.SerializeToElement(CreatePlugin().Configuration, JsonDefaults.PascalCaseOptions);

        Assert.Equal(clientId, settings.GetProperty("TraktClientId").GetString());
        Assert.Equal(expectedUrls, settings.GetProperty("SourceUrls").Deserialize<string[]>());
        Assert.False(settings.GetProperty("PosterIcon").GetBoolean());
    }

    [Fact]
    public void Reloading_legacy_arr_settings_preserves_bases_and_keys_in_one_list()
    {
        var plugin = CreatePlugin();
        Directory.CreateDirectory(Path.GetDirectoryName(plugin.ConfigurationFilePath)!);
        File.WriteAllText(plugin.ConfigurationFilePath, """
            <PluginConfiguration>
              <SourceUrls><string>https://media.example.com/radarr/watched</string></SourceUrls>
              <RadarrServers>
                <ArrServerConfiguration><BaseUrl>https://media.example.com/radarr</BaseUrl><ApiKey>movie-key</ApiKey></ArrServerConfiguration>
              </RadarrServers>
              <SonarrServers>
                <ArrServerConfiguration><BaseUrl>https://media.example.com/sonarr</BaseUrl><ApiKey>series-key</ApiKey></ArrServerConfiguration>
              </SonarrServers>
            </PluginConfiguration>
            """);

        var reloaded = CreatePlugin();
        var dashboard = JsonSerializer.SerializeToElement(reloaded.Configuration, JsonDefaults.PascalCaseOptions);
        Assert.Equal([("https://media.example.com/radarr", "movie-key"), ("https://media.example.com/sonarr", "series-key")],
            dashboard.GetProperty("ArrServers").EnumerateArray()
                .Select(server => (server.GetProperty("BaseUrl").GetString(), server.GetProperty("ApiKey").GetString())));
        Assert.False(dashboard.TryGetProperty("RadarrServers", out _));
        Assert.False(dashboard.TryGetProperty("SonarrServers", out _));

        reloaded.UpdateConfiguration(reloaded.Configuration);
        var xml = File.ReadAllText(reloaded.ConfigurationFilePath);
        Assert.Contains("<ArrServers>", xml);
        Assert.DoesNotContain("RadarrServers", xml);
        Assert.DoesNotContain("SonarrServers", xml);
        Assert.Equal(["https://media.example.com/radarr/watched"], CreatePlugin().Configuration.SourceUrls);
    }

    [Fact]
    public void Saving_settings_round_trips_the_arr_server_list_separately_from_source_URLs()
    {
        const string source = "https://radarr.example.com/family-movies";
        var plugin = CreatePlugin();
        IHasPluginConfiguration configuredPlugin = plugin;
        using var update = JsonDocument.Parse($$"""
            {
              "ArrServers": [
                { "BaseUrl": "https://radarr.example.com", "ApiKey": "radarr-key" },
                { "BaseUrl": "http://localhost:7878/radarr", "ApiKey": "local-radarr-key" },
                { "BaseUrl": "https://sonarr.example.com", "ApiKey": "sonarr-key" }
              ],
              "SourceUrls": ["{{source}}"]
            }
            """);
        var config = Assert.IsType<PluginConfiguration>(
            update.Deserialize(configuredPlugin.ConfigurationType, JsonDefaults.Options));

        configuredPlugin.UpdateConfiguration(config);
        var reloaded = JsonSerializer.SerializeToElement(CreatePlugin().Configuration, JsonDefaults.PascalCaseOptions);

        Assert.Equal(source, Assert.Single(reloaded.GetProperty("SourceUrls").EnumerateArray()).GetString());
        Assert.Equal(
            [("https://radarr.example.com", "radarr-key"), ("http://localhost:7878/radarr", "local-radarr-key"),
                ("https://sonarr.example.com", "sonarr-key")],
            reloaded.GetProperty("ArrServers").EnumerateArray().Select(server =>
            {
                Assert.Equal(2, server.EnumerateObject().Count());
                return (server.GetProperty("BaseUrl").GetString(), server.GetProperty("ApiKey").GetString());
            }));
    }

    [Fact]
    public void A_supplied_empty_arr_list_overrides_legacy_pairs_in_a_configuration_update()
    {
        using var update = JsonDocument.Parse("""
            {
              "ArrServers": [],
              "RadarrServers": [{ "BaseUrl": "https://old.example.com", "ApiKey": "previous-key" }]
            }
            """);
        var plugin = CreatePlugin();

        plugin.UpdateConfiguration(update.Deserialize<PluginConfiguration>(JsonDefaults.Options)!);

        Assert.Empty(CreatePlugin().Configuration.ArrServers);
        Assert.DoesNotContain("previous-key", File.ReadAllText(plugin.ConfigurationFilePath));
    }

    [Theory]
    [InlineData("https://MEDIA.example.com:443/Radarr/", "private-key", "already used")]
    [InlineData("https://media.example.com/Radarr//proxy", "private-key", "base URL")]
    [InlineData("https://media.example.com/proxy/%2e%2e/private-path", "private-key", "base URL")]
    [InlineData("http://[fe80::1%25eth0]/private-path", "private-key", "base URL")]
    [InlineData("https://media.example.com/Sonarr?apikey=private-key", "private-key", "base URL")]
    [InlineData("https://media.example.com/Sonarr", "private-key\r\ninvalid", "API key")]
    [InlineData("https://media.example.com/Sonarr", "", "API key")]
    public void Saving_unusable_arr_settings_identifies_the_row_without_persisting_or_echoing_values(
        string baseUrl, string key, string expectedProblem)
    {
        var plugin = CreatePlugin();
        IHasPluginConfiguration configuredPlugin = plugin;
        configuredPlugin.UpdateConfiguration(new PluginConfiguration { SourceUrls = ["https://example.com/previous"] });
        var config = new PluginConfiguration
        {
            ArrServers =
            [
                new() { BaseUrl = "https://media.example.com/Radarr", ApiKey = "existing-key" },
                new() { BaseUrl = "https://media.example.com/other", ApiKey = "other-key" },
                new() { BaseUrl = baseUrl, ApiKey = key }
            ]
        };

        var error = Assert.ThrowsAny<ArgumentException>(() => configuredPlugin.UpdateConfiguration(config));

        Assert.Contains("*arr server 3", error.Message, StringComparison.Ordinal);
        Assert.Contains(expectedProblem, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(["https://example.com/previous"], plugin.Configuration.SourceUrls);
        Assert.Equal(["https://example.com/previous"], CreatePlugin().Configuration.SourceUrls);
    }

    [Theory]
    [InlineData("https://MEDIA.example.com:443/Radarr/movie-night?apikey=private-query")]
    [InlineData("https://admin:private-userinfo@media.example.com/Radarr/movie-night")]
    [InlineData("https://@media.example.com/Radarr/movie-night")]
    [InlineData("https://media.example.com/Radarr/movie-night#private-fragment")]
    public void Saving_a_decorated_arr_source_rejects_it_without_persisting_or_echoing_the_URL(string source)
    {
        var plugin = CreatePlugin();
        IHasPluginConfiguration configuredPlugin = plugin;
        const string unrelated = "https://media.example.com/Radarr-archive/watch?apikey=ordinary-query";
        var config = new PluginConfiguration
        {
            SourceUrls = [unrelated],
            ArrServers = [new() { BaseUrl = "https://media.example.com/Radarr", ApiKey = "configured-key" }]
        };
        configuredPlugin.UpdateConfiguration(config);

        var ex = Assert.Throws<ArgumentException>(() => configuredPlugin.UpdateConfiguration(new PluginConfiguration
        {
            ArrServers = config.ArrServers,
            SourceUrls = [unrelated, source]
        }));

        Assert.Contains("Source list URL 2", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", ex.Message, StringComparison.Ordinal);
        Assert.Equal([unrelated], CreatePlugin().Configuration.SourceUrls);
    }

    [Fact]
    public async Task Sync_logs_unsupported_URLs_without_userinfo_query_or_fragment()
    {
        var plugin = CreatePlugin();
        plugin.UpdateConfiguration(new PluginConfiguration
        {
            SourceUrls =
            [
                "https://name:private-password@app.trakt.tv/users/justin/lists/open-your-eyes?access_token=private-query#private-fragment",
                "not a URL#private-invalid"
            ]
        });
        var logger = new TaskLog();
        using var http = new HttpClient(new RecordedHttpResponses());
        var task = new SyncTask(new CollectionSync([new TraktHandler(http, () => plugin.Configuration)], new ControlledLibrary()), logger);

        await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains("https://app.trakt.tv/users/justin/lists/open-your-eyes", logger.Warnings[0], StringComparison.Ordinal);
        Assert.All(logger.Warnings, warning => Assert.DoesNotContain("private-", warning, StringComparison.Ordinal));
    }

    public void Dispose() => _data.Delete(recursive: true);

    private Plugin CreatePlugin() => new(new PluginTestServices.TestApplicationPaths(_data.FullName), new ConfigurationXmlSerializer());

    private sealed class ConfigurationXmlSerializer : IXmlSerializer
    {
        public object DeserializeFromStream(Type type, Stream stream) => new XmlSerializer(type).Deserialize(stream)!;
        public void SerializeToStream(object obj, Stream stream) => new XmlSerializer(obj.GetType()).Serialize(stream, obj);

        public void SerializeToFile(object obj, string file)
        {
            using var stream = File.Create(file);
            SerializeToStream(obj, stream);
        }

        public object DeserializeFromFile(Type type, string file)
        {
            using var stream = File.OpenRead(file);
            return DeserializeFromStream(type, stream);
        }

        public object DeserializeFromBytes(Type type, byte[] buffer) => throw new NotSupportedException();
    }

    private sealed class TaskLog : ILogger<SyncTask>
    {
        public List<string> Warnings { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

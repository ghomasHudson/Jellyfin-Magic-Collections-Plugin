using Jellyfin.Plugin.MagicCollections.Configuration;
using Jellyfin.Plugin.MagicCollections.Sync;
using Xunit;

namespace MagicCollections.Tests;

internal sealed class ArrSyncFixture : IDisposable
{
    private readonly HttpClient _http;

    public PluginConfiguration Configuration { get; set; }
    public ControlledLibrary Library { get; } = new();
    public RecordedHttpResponses Responses { get; }
    public List<(string Url, string Key)> Requests { get; } = [];
    public CollectionSync Engine { get; }

    public ArrSyncFixture(PluginConfiguration configuration)
    {
        Configuration = configuration;
        Responses = new(request => Requests.Add((request.RequestUri!.AbsoluteUri,
            Assert.Single(request.Headers.GetValues("X-Api-Key")))));
        _http = new(Responses);
        Engine = new([new ArrHandler(_http, () => Configuration.ArrServers)], Library);
    }

    public Task Sync(params string[] urls) =>
        Engine.RunAsync(urls, url => Assert.Fail($"Unexpected unsupported URL: {url}"), default);

    public Guid AddMedia(SourceMediaType type, string provider, string id)
    {
        var item = Guid.NewGuid();
        Library.Media.Add(new(item, new(type, new Dictionary<string, string> { [provider] = id })));
        return item;
    }

    public void Dispose() => _http.Dispose();
}

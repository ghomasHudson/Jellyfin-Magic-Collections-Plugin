using Jellyfin.Plugin.MagicCollections.Configuration;

namespace Jellyfin.Plugin.MagicCollections.Sync;

internal sealed record ArrServiceDefinition(
    string Name, Version MinimumVersion, string MediaEndpoint, SourceMediaType MediaType,
    IReadOnlyList<ArrProviderField> ProviderFields)
{
    public static ArrServiceDefinition Radarr { get; } =
        new("Radarr", new(4, 0, 5), "api/v3/movie", SourceMediaType.Movie,
            [new("tmdbId", "Tmdb", Primary: true), new("imdbId", "Imdb", ArrProviderIdKind.Imdb)]);
    public static ArrServiceDefinition Sonarr { get; } =
        new("Sonarr", new(4, 0), "api/v3/series", SourceMediaType.Series,
            [new("tvdbId", "Tvdb", Primary: true), new("imdbId", "Imdb", ArrProviderIdKind.Imdb), new("tmdbId", "Tmdb")]);

    public string MinimumRequirement => $"{Name} {MinimumVersion} or newer";
}

internal enum ArrProviderIdKind { Numeric, Imdb }
internal sealed record ArrProviderField(string Property, string Provider,
    ArrProviderIdKind Kind = ArrProviderIdKind.Numeric, bool Primary = false);

internal sealed class ArrHandler(HttpClient http, Func<ArrServerConfiguration[]> getServers) : ISourceListHandler
{
    private static readonly Uri UnresolvedSource = new("about:blank");
    private Dictionary<Uri, ArrServiceDefinition>? _detectedServers;

    public string SourceTag => "*arr";

    public bool Supports(Uri url) => Resolve(url) is not null;

    public bool SupportsDraft(Uri url, IEnumerable<string?> bases) => ArrTagSource.SupportsDraft(url, bases);

    public Uri GetCanonicalUrl(Uri url) => RequireSource(url).CanonicalUrl;

    public async Task<SourceList> ReadAsync(Uri url, CancellationToken cancellationToken)
    {
        var source = RequireSource(url);
        if (_detectedServers is null || !_detectedServers.TryGetValue(source.BaseUrl, out var definition))
        {
            definition = await ArrTagReader.DetectAsync(http, source.BaseUrl, source.ApiKey, cancellationToken).ConfigureAwait(false);
            _detectedServers?.Add(source.BaseUrl, definition);
        }
        return await ArrTagReader.ReadAsync(http, source, definition, cancellationToken).ConfigureAwait(false);
    }

    public ArrHandler Snapshot(PluginConfiguration? snapshot)
    {
        var servers = snapshot?.ArrServers ?? getServers();
        // Only the sequential sync instance caches successful detection. Live handlers never share a cache.
        return new(http, () => servers) { _detectedServers = [] };
    }

    private ArrTagSource? Resolve(Uri url) => ArrTagSource.Resolve(url, getServers());

    private ArrTagSource RequireSource(Uri url) =>
        Resolve(url) ?? throw new SourceListReadException(UnresolvedSource,
            $"Not a tag URL for a uniquely configured {SourceTag} instance.");
}

using Jellyfin.Plugin.MagicCollections.Sync;

namespace Jellyfin.Plugin.MagicCollections.Configuration;

internal static class ArrServerValidation
{
    internal const string InvalidKey = "Enter an API key with no spaces or non-ASCII characters.";

    public static void EnsureValid(PluginConfiguration config)
    {
        var servers = config.ArrServers;
        var bases = new HashSet<(string Origin, string Path)>();
        if (CheckBases(servers.Select(row => row?.BaseUrl), bases) is { } error)
        {
            throw new ArgumentException($"*arr server {error.Index + 1}: {error.Message}");
        }

        for (var index = 0; index < servers.Length; index++)
        {
            if (ArrTagSource.ReadApiKey(servers[index]?.ApiKey) is null)
            {
                throw new ArgumentException($"*arr server {index + 1}: {InvalidKey}");
            }
        }

        if (bases.Count == 0)
        {
            return;
        }
        var urls = config.SourceUrls ?? [];
        for (var index = 0; index < urls.Length; index++)
        {
            if (ArrTagSource.IsDecoratedTagUrl(urls[index], bases))
            {
                throw new ArgumentException($"Source list URL {index + 1}: Remove credentials, query strings, and fragments from *arr tag URLs.");
            }
        }
    }

    public static ArrBaseError? FindInvalidBase(IEnumerable<string?>? bases) => CheckBases(bases ?? [], []);

    private static ArrBaseError? CheckBases(IEnumerable<string?> bases,
        HashSet<(string Origin, string Path)> seen)
    {
        var index = 0;
        foreach (var baseUrl in bases)
        {
            if (CheckBase(baseUrl, seen, out _) is { } message)
            {
                return new(index, message);
            }
            index++;
        }
        return null;
    }

    internal static string? CheckBase(string? baseUrl, HashSet<(string Origin, string Path)> seen,
        out (string Origin, string Path) location)
    {
        if (ArrTagSource.ParseBase(baseUrl) is not { } parsed)
        {
            location = default;
            return "Enter a valid HTTP or HTTPS base URL without credentials, a query string, a fragment, or invalid path segments.";
        }
        location = parsed;
        return seen.Add(location) ? null : "This base URL is already used by another Radarr or Sonarr server.";
    }
}

internal sealed record ArrBaseError(int Index, string Message);

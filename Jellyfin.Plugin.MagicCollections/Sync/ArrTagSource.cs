using Jellyfin.Plugin.MagicCollections.Configuration;

namespace Jellyfin.Plugin.MagicCollections.Sync;

internal sealed class ArrTagSource(Uri baseUrl, string label, string apiKey)
{
    public Uri BaseUrl { get; } = baseUrl;
    public string Label { get; } = label;
    public string ApiKey { get; } = apiKey;
    public Uri CanonicalUrl => new(BaseUrl, Uri.EscapeDataString(Label));

    public static ArrTagSource? Resolve(Uri url, ArrServerConfiguration[] servers)
    {
        var match = Match(url, servers.Select(server => (server?.BaseUrl, server)));
        if (match is not { } source || ReadApiKey(source.Value?.ApiKey) is not { } key)
        {
            return null;
        }
        return new(source.BaseUrl, source.Label, key);
    }

    public static bool SupportsDraft(Uri url, IEnumerable<string?> bases) =>
        Match(url, bases.Select(value => (value, true))) is not null;

    internal static string? ReadApiKey(string? rawKey) =>
        rawKey?.Trim() is { Length: > 0 } key && key.All(c => c is >= '!' and <= '~') ? key : null;

    internal static (string Origin, string Path)? ParseBase(string? rawBaseUrl) =>
        Uri.TryCreate(rawBaseUrl?.Trim(), UriKind.Absolute, out var configuredUrl) &&
            GetLocation(configuredUrl) is { } location
            ? (location.Origin, location.Path.TrimEnd('/') + "/")
            : null;

    internal static bool IsDecoratedTagUrl(string? value, IEnumerable<(string Origin, string Path)> bases)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") ||
            url.Host.Length == 0 ||
            (!GetAuthorityAndPath(url).Authority.Contains('@') && url.Query.Length == 0 && url.Fragment.Length == 0))
        {
            return false;
        }

        var (origin, path) = NormalizedLocation(url);
        return bases.Any(configured => configured.Origin == origin &&
            TagSegment(path.TrimEnd('/'), configured.Path) is not null);
    }

    private static (Uri BaseUrl, string Label, T Value)? Match<T>(Uri url,
        IEnumerable<(string? BaseUrl, T Value)> bases)
    {
        if (GetLocation(url) is not { } location)
        {
            return null;
        }

        (Uri BaseUrl, string Label, T Value)? match = null;
        foreach (var (rawBaseUrl, value) in bases)
        {
            if (ParseBase(rawBaseUrl) is not { } configured || location.Origin != configured.Origin ||
                TagSegment(location.Path, configured.Path) is not { } segment)
            {
                continue;
            }

            // Any second matching row (even one without a usable key) makes the source ambiguous.
            if (match is not null)
            {
                return null;
            }

            var label = Uri.UnescapeDataString(segment);
            if (string.IsNullOrWhiteSpace(label) || label.Any(char.IsControl))
            {
                return null;
            }
            match = (new(configured.Origin + configured.Path), label, value);
        }
        return match;
    }

    private static string? TagSegment(string path, string basePath)
    {
        if (path.Length <= basePath.Length || !path.StartsWith(basePath, StringComparison.Ordinal))
        {
            return null;
        }
        var segment = path[basePath.Length..];
        return segment.Contains('/') ? null : segment;
    }

    private static (string Origin, string Path)? GetLocation(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || url.Host.Length == 0 ||
            !url.IsWellFormedOriginalString() || url.UserInfo.Length != 0 ||
            url.Query.Length != 0 || url.Fragment.Length != 0)
        {
            return null;
        }

        // Uri normalizes dot segments before exposing AbsolutePath. Check the original path first
        // so a URL cannot contain extra segments that disappear before matching a configured base.
        var (authority, rawPath) = GetAuthorityAndPath(url);
        // UriBuilder drops IPv6 zone identifiers; never match or send to a different interface.
        if (authority.Length == 0 || authority.Contains('@') ||
            (url.HostNameType == UriHostNameType.IPv6 && authority.Contains('%')) ||
            rawPath.Contains("//", StringComparison.Ordinal) ||
            rawPath.Split('/').Any(segment => Uri.UnescapeDataString(segment) is "." or ".."))
        {
            return null;
        }

        return NormalizedLocation(url);
    }

    private static (string Authority, string Path) GetAuthorityAndPath(Uri url)
    {
        var original = url.OriginalString;
        var delimiter = original.IndexOf("://", StringComparison.Ordinal);
        if (delimiter < 0)
        {
            return (string.Empty, string.Empty);
        }
        var authorityStart = delimiter + 3;
        var slash = original.IndexOfAny(['/', '\\', '?', '#'], authorityStart);
        return slash < 0 ? (original[authorityStart..], string.Empty) :
            (original[authorityStart..slash], original[slash..]);
    }

    private static (string Origin, string Path) NormalizedLocation(Uri url)
    {
        var origin = new UriBuilder(url.Scheme, url.IdnHost, url.IsDefaultPort ? -1 : url.Port)
            .Uri.GetLeftPart(UriPartial.Authority);
        // Equivalent spellings of an escaped byte should not affect matching; literal path case does.
        var path = string.Create(url.AbsolutePath.Length, url.AbsolutePath, (result, value) =>
        {
            value.AsSpan().CopyTo(result);
            for (var index = 0; index < result.Length - 2; index++)
            {
                if (result[index] == '%')
                {
                    result[index + 1] = char.ToUpperInvariant(result[index + 1]);
                    result[index + 2] = char.ToUpperInvariant(result[index + 2]);
                    index += 2;
                }
            }
        });
        return (origin, path);
    }
}

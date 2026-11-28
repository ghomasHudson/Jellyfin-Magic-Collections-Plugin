namespace Jellyfin.Plugin.MagicCollections.Sync;

internal static class SourceListHandlerLookup
{
    public static (Uri Source, ISourceListHandler Handler)? Find(IEnumerable<ISourceListHandler> handlers, string? url,
        Func<ISourceListHandler, Uri, bool>? supports = null) =>
        Uri.TryCreate(url, UriKind.Absolute, out var source) &&
            handlers.FirstOrDefault(handler => supports?.Invoke(handler, source) ?? handler.Supports(source)) is { } handler
            ? (source, handler)
            : null;
}

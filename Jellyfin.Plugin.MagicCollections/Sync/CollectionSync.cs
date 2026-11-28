using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.MagicCollections.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MagicCollections.Sync;

public sealed class CollectionSync(IEnumerable<ISourceListHandler> handlers, ICollectionLibrary library)
{
    internal const string ManagedCollectionTag = "Magic Collection";
    private readonly JellyfinQueryHandler? _jellyfin;

    internal CollectionSync(IEnumerable<ISourceListHandler> handlers, ICollectionLibrary library, JellyfinQueryHandler jellyfin)
        : this(handlers, library) => _jellyfin = jellyfin;

    public Task RunAsync(IEnumerable<string> urls, Action<string> reportUnsupported,
        CancellationToken cancellationToken, ILogger? verboseLogger = null,
        Action<string>? reportPrivateFailure = null, Action<string>? reportJellyfinFailure = null) =>
        RunAsync(urls, reportUnsupported, cancellationToken, verboseLogger, reportPrivateFailure,
            configuration: null, reportJellyfinFailure: reportJellyfinFailure);

    internal async Task RunAsync(IEnumerable<string> urls, Action<string> reportUnsupported,
        CancellationToken cancellationToken, ILogger? verboseLogger,
        Action<string>? reportPrivateFailure, PluginConfiguration? configuration, Action<string>? reportJellyfinFailure = null)
    {
        // Dashboard saves replace the settings object. Use the same *arr settings for selection,
        // canonicalization and the read, even if a save lands during the run.
        var runHandlers = handlers.Select(handler => handler is ArrHandler arr ? arr.Snapshot(configuration) : handler).ToArray();
        // Snapshot once per run. Build each index only if a source uses it.
        var media = new Lazy<LibraryMedia[]>(() => library.GetMedia().ToArray());
        var mediaByProvider = new Lazy<ILookup<MatchKey, Guid>>(() =>
            media.Value.SelectMany(item => Keys(item.Identity), (item, key) => (key, item.Id))
                .ToLookup(pair => pair.key, pair => pair.Id));
        var moviesByTitleYear = new Lazy<ILookup<MovieTitleYear, Guid>>(() =>
            media.Value.Where(item => item.Identity.Type == SourceMediaType.Movie &&
                    item.MovieTitleYear is { IsComplete: true })
                .ToLookup(item => item.MovieTitleYear!.Value.MatchKey(), item => item.Id));
        var syncedSources = new HashSet<(string Key, bool IsArr)>();

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _jellyfin is { } jellyfin && JellyfinQuery.ParseSource(url) is { } local
                ? ReadLocal(jellyfin, local.Query, local.Error)
                : await ReadExternalAsync(url).ConfigureAwait(false);
            if (result is null)
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var libraryMatchCount = result.MatchedIds.Count;
            // The same URL can point to another *arr service in a later run without reusing the old collection.
            var existing = library.FindByTags(result.SourceTag, ManagedCollectionTag, result.ArrServiceTag);
            if (existing is null && result.PreviousTag is { } previousTag &&
                library.FindByTags(previousTag, ManagedCollectionTag) is { } previous)
            {
                // Changing the source's access mode must not create a second collection.
                // Only migrate after successfully reading the entire list.
                await library.ReplaceTagAsync(previous.Id, previousTag, result.SourceTag, cancellationToken).ConfigureAwait(false);
                existing = previous;
            }

            if (existing is null && libraryMatchCount == 0)
            {
                verboseLogger?.LogInformation(
                    "No library matches for {SourceUrl} ({MatchableEntryCount} entries available for matching); collection not created",
                    result.LogLabel, result.MatchableEntryCount);
                continue;
            }

            var id = existing?.Id ?? await library.CreateAsync(
                result.Name, result.Description, ManagedCollectionTag, result.SourceTag, result.ArrServiceTag ?? result.HandlerTag)
                .ConfigureAwait(false);
            if (existing is not null && !string.IsNullOrWhiteSpace(result.Description))
            {
                await library.EnsureOverviewAsync(id, result.Description, cancellationToken).ConfigureAwait(false);
            }
            var currentMembers = existing?.MemberIds ?? [];
            var additions = result.MatchedIds.Except(currentMembers).ToArray();
            var removals = currentMembers.Except(result.MatchedIds).ToArray();
            if (additions.Length != 0)
            {
                await library.AddMembersAsync(id, additions).ConfigureAwait(false);
            }
            if (removals.Length != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await library.RemoveMembersAsync(id, removals).ConfigureAwait(false);
            }
            verboseLogger?.LogInformation(
                "Collection membership for {SourceUrl}: {LibraryMatchCount} library matches from {MatchableEntryCount} entries available for matching; added {AddedCount} items, removed {RemovedCount} items (collection created: {Created})",
                result.LogLabel, libraryMatchCount, result.MatchableEntryCount, additions.Length, removals.Length, existing is null);

            await library.EnsurePosterAsync(id, result.FaviconUrls, result.ImageUrl, cancellationToken).ConfigureAwait(false);
        }

        MatchedSource? ReadLocal(JellyfinQueryHandler jellyfin, JellyfinQuery? query, string? error)
        {
            if (query is null)
            {
                var message = $"Invalid Jellyfin library query: {error}";
                if (reportJellyfinFailure is null) throw new ArgumentException(message);
                reportJellyfinFailure(message);
                return null;
            }
            var sourceTag = "jellyfin-query:" + Hash(query.CanonicalSource);
            if (!BeginSource((sourceTag, false), sourceTag))
            {
                return null;
            }

            Guid[] matches;
            string? libraryName;
            try
            {
                (matches, libraryName) = jellyfin.Read(query, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && reportJellyfinFailure is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reportJellyfinFailure($"Jellyfin library query failed ({sourceTag}): {ex.Message}");
                return null;
            }
            var (title, overview) = query.CollectionMetadata(libraryName);
            return new(sourceTag, "Jellyfin", sourceTag, title, overview,
                [], null, matches.Length, matches);
        }

        async Task<MatchedSource?> ReadExternalAsync(string? url)
        {
            if (SourceListHandlerLookup.Find(runHandlers, url) is not { } match)
            {
                reportUnsupported(url!);
                return null;
            }
            var (source, handler) = match;
            var handlerTag = handler.SourceTag;
            var canonicalSource = handler.GetCanonicalUrl(source);
            var canonicalUrl = canonicalSource.AbsoluteUri;
            // A personal list may be private; don't wait for the API to find out before redacting it.
            var opaqueTag = handler is TraktHandler trakt && trakt.IsPersonalList(canonicalSource)
                ? PrivateTraktTag(canonicalUrl) : null;
            var logLabel = opaqueTag ?? (verboseLogger is null ? null :
                canonicalSource.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped));
            // An *arr base resolves to the same service throughout this run.
            if (!BeginSource((canonicalUrl, handler is ArrHandler), logLabel))
            {
                return null;
            }

            SourceList list;
            try
            {
                list = await handler.ReadAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch (SourceListReadException ex) when (ex.IsPrivateTraktFailure && reportPrivateFailure is not null)
            {
                reportPrivateFailure($"{opaqueTag}: {ex.Reason}");
                return null;
            }

            var matches = list.ProviderItems.SelectMany(Keys)
                .SelectMany(key => mediaByProvider.Value[key]).ToHashSet();
            foreach (var movie in list.MoviesByTitleYear.Where(movie => movie.IsComplete))
            {
                var candidates = moviesByTitleYear.Value[movie.MatchKey()].Distinct().Take(2).ToArray();
                if (candidates.Length == 1)
                {
                    matches.Add(candidates[0]);
                }
            }
            // Watchlist and recommendation URLs refer to the connected account's own results.
            var sourceTag = list.TraktAccountScopedOwner is { } owner
                ? PrivateTraktTag($"{canonicalUrl}|{owner.ToLowerInvariant()}")
                : list.ReadWithTraktAccount ? opaqueTag! : canonicalUrl;
            var previousTag = opaqueTag is not null && list.TraktAccountScopedOwner is null
                ? list.ReadWithTraktAccount ? canonicalUrl : opaqueTag
                : null;
            return new(sourceTag, handlerTag, logLabel, CollectionName(list, source), list.Description,
                list.FaviconUrls, list.ImageUrl, list.ProviderItems.Count + list.MoviesByTitleYear.Count, matches,
                previousTag, list.ArrServiceTag);
        }

        bool BeginSource((string Key, bool IsArr) deduplicationKey, string? logLabel)
        {
            if (!syncedSources.Add(deduplicationKey))
            {
                verboseLogger?.LogInformation("Skipping duplicate source list {SourceUrl}", logLabel);
                return false;
            }
            verboseLogger?.LogInformation("Reading source list {SourceUrl}", logLabel);
            return true;
        }
    }

    private static string PrivateTraktTag(string canonicalUrl) =>
        "trakt-private:" + Hash(canonicalUrl);

    private static string Hash(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private static string CollectionName(SourceList list, Uri source)
    {
        if (!string.IsNullOrWhiteSpace(list.Name))
        {
            return list.Name.Trim();
        }
        var slug = Uri.UnescapeDataString(source.Segments.Last().Trim('/'));
        var words = slug.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? source.Host : string.Join(' ', words);
    }

    private sealed record MatchedSource(string SourceTag, string HandlerTag, string? LogLabel, string Name,
        string? Description, IReadOnlyList<Uri> FaviconUrls, Uri? ImageUrl, int MatchableEntryCount,
        IReadOnlyCollection<Guid> MatchedIds, string? PreviousTag = null, string? ArrServiceTag = null);

    private readonly record struct MatchKey(SourceMediaType Type, string Provider, string ProviderId);

    private static IEnumerable<MatchKey> Keys(MediaIdentity identity)
    {
        foreach (var (provider, id) in identity.ProviderIds)
        {
            if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(id))
            {
                yield return new(identity.Type, provider.ToUpperInvariant(), id);
            }
        }
    }
}

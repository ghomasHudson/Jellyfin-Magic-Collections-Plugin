using System.Net.Mime;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MagicCollections.Configuration;
using Jellyfin.Plugin.MagicCollections.Sync;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MagicCollections;

internal sealed class JellyfinCollectionLibrary(
    ILibraryManager library, ICollectionManager collections, IProviderManager providers,
    CollectionImageClient images, Func<PluginConfiguration> getConfiguration) : ICollectionLibrary
{
    public IEnumerable<LibraryMedia> GetMedia()
    {
        var items = library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            GroupByPresentationUniqueKey = false
        }, allowExternalContent: false);
        return items.Select(item =>
        {
            var type = item is Movie ? SourceMediaType.Movie : SourceMediaType.Series;
            MovieTitleYear? titleYear = type == SourceMediaType.Movie &&
                item.ProductionYear is int year && year > 0 && !string.IsNullOrWhiteSpace(item.Name)
                ? new(item.Name, year)
                : null;
            return new LibraryMedia(item.Id, new(type, item.ProviderIds), titleYear);
        });
    }

    public TaggedCollection? FindByTags(string urlTag, string markerTag, string? serviceTag = null)
    {
        var candidates = library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            GroupByPresentationUniqueKey = false,
            Tags = [urlTag]
        });
        var collection = candidates.OfType<BoxSet>().SingleOrDefault(c =>
            c.Tags.Contains(urlTag, StringComparer.Ordinal) && c.Tags.Contains(markerTag, StringComparer.Ordinal) &&
            (serviceTag is null || c.Tags.Contains(serviceTag, StringComparer.Ordinal)));
        return collection is null ? null : new(collection.Id, collection.GetLinkedChildren().Select(item => item.Id).ToArray());
    }

    public async Task<Guid> CreateAsync(string name, string? overview, params string[] tags)
    {
        // Jellyfin derives the box set's folder and ID from the creation name.
        // Use a private storage name so a display namesake cannot collide with it.
        var collection = await collections.CreateCollectionAsync(new CollectionCreationOptions
        {
            Name = $"MagicCollections-{Guid.NewGuid():N}"
        }).ConfigureAwait(false);
        collection.Tags = [.. collection.Tags, .. tags.Except(collection.Tags, StringComparer.Ordinal)];
        collection.LockedFields = [.. collection.LockedFields, MetadataField.Name, MetadataField.Tags];
        if (!string.IsNullOrWhiteSpace(overview))
        {
            SetManagedOverview(collection, overview);
        }
        collection.Name = name;
        await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        return collection.Id;
    }

    public Task AddMembersAsync(Guid id, IReadOnlyCollection<Guid> memberIds) =>
        collections.AddToCollectionAsync(id, memberIds);

    public Task RemoveMembersAsync(Guid id, IReadOnlyCollection<Guid> memberIds) =>
        collections.RemoveFromCollectionAsync(id, memberIds);

    public Task ReplaceTagAsync(Guid id, string oldTag, string newTag, CancellationToken cancellationToken)
    {
        if (library.GetItemById(id) is not BoxSet collection || !collection.Tags.Contains(oldTag, StringComparer.Ordinal))
        {
            return Task.CompletedTask;
        }

        collection.Tags = [.. collection.Tags.Where(tag => tag != oldTag && tag != newTag), newTag];
        return collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
    }

    public Task EnsureOverviewAsync(Guid id, string overview, CancellationToken cancellationToken)
    {
        if (library.GetItemById(id) is not BoxSet collection || !string.IsNullOrWhiteSpace(collection.Overview))
        {
            return Task.CompletedTask;
        }

        SetManagedOverview(collection, overview);
        return collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
    }

    private static void SetManagedOverview(BoxSet collection, string overview)
    {
        // Jellyfin refreshes box sets after creation and membership changes.
        if (!collection.LockedFields.Contains(MetadataField.Overview))
        {
            collection.LockedFields = [.. collection.LockedFields, MetadataField.Overview];
        }
        collection.Overview = overview;
    }

    public async Task EnsurePosterAsync(Guid id, IReadOnlyList<Uri> faviconUrls, Uri? listImageUrl, CancellationToken cancellationToken)
    {
        if (library.GetItemById(id) is not BoxSet collection || HasCurrentPoster(collection))
        {
            return;
        }

        var config = getConfiguration();
        if (!config.PosterBackgroundImage && !config.PosterIcon && !config.PosterListName)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var logo = config.PosterIcon
            ? await images.TryLoadFaviconAsync(faviconUrls, cancellationToken).ConfigureAwait(false)
            : null;
        using var background = config.PosterBackgroundImage
            ? await images.TryLoadListImageAsync(listImageUrl, cancellationToken).ConfigureAwait(false)
            : null;
        cancellationToken.ThrowIfCancellationRequested();
        using var poster = CollectionPoster.Render(config.PosterListName ? collection.Name : string.Empty, logo, background);

        // Jellyfin's SaveImage isn't conditional. Recheck after rendering to narrow the
        // race with an upload, but another writer can still enter after this check.
        cancellationToken.ThrowIfCancellationRequested();
        if (library.GetItemById(id) is not BoxSet current || HasCurrentPoster(current))
        {
            return;
        }

        await providers.SaveImage(current, poster, MediaTypeNames.Image.Png, ImageType.Primary, null, cancellationToken)
            .ConfigureAwait(false);
        await current.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, CancellationToken.None).ConfigureAwait(false);
    }

    private static bool HasCurrentPoster(BoxSet collection)
    {
        var image = collection.GetImageInfo(ImageType.Primary, 0);
        if (image is null)
        {
            return false;
        }

        var path = image.Path;
        if (!image.IsLocalFile || !Path.IsPathFullyQualified(path))
        {
            return true;
        }

        try
        {
            // File.Exists also returns false for access and I/O errors.
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }
}

using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MagicCollections.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.MagicCollections;

internal sealed class JellyfinQueryHandler(ILibraryManager library, IFileSystem fileSystem)
{
    public (Guid[] Matches, string? LibraryName) Read(JellyfinQuery source, CancellationToken cancellationToken)
    {
        var (roots, libraryName) = FindScope(source.ParentId, cancellationToken);
        if (roots.Length == 0)
        {
            return ([], libraryName);
        }

        var studioIds = new HashSet<Guid>();
        foreach (var studio in source.Studios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Resolve names from the index; GetStudio(name) could create a new studio.
            studioIds.UnionWith(library.GetItemIds(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Studio],
                Name = studio
            }));
        }
        if (source.Studios.Length != 0 && studioIds.Count == 0)
        {
            return ([], libraryName);
        }

        var query = new InternalItemsQuery
        {
            // A physical library path can itself be nested under another configured path.
            AncestorIds = roots,
            IncludeItemTypes = source.ItemTypes,
            IsVirtualItem = false,
            IsPlaceHolder = false,
            GroupByPresentationUniqueKey = false,
            GroupBySeriesPresentationUniqueKey = false,
            StudioIds = studioIds.ToArray()
        };
        source.ApplyNativeFilters(query);
        var items = library.GetItemList(query, allowExternalContent: false);

        var ids = new HashSet<Guid>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is Movie or Series && item.ExtraType is null && !item.IsShortcut && item.ChannelId == Guid.Empty &&
                item.Path is not null && Path.IsPathFullyQualified(item.Path) &&
                MatchesItemProperties(item, source))
            {
                ids.Add(item.Id);
            }
        }
        if (source.Limit is not { } limit)
        {
            return (ids.ToArray(), libraryName);
        }

        var selected = ids.ToArray();
        if (source.Random)
        {
            Random.Shared.Shuffle(selected);
        }
        else
        {
            Array.Sort(selected);
        }
        return (selected.Length <= limit ? selected : selected[..limit], libraryName);
    }

    // Jellyfin 10.11 ignores MinCriticRating, searches serialized text for series status and 3D,
    // and can count non-videos as having no subtitles. Use the actual item properties before limiting.
    private static bool MatchesItemProperties(BaseItem item, JellyfinQuery source) =>
        (source.MinCriticRating is not { } minimum || item.CriticRating >= minimum) &&
        (source.SeriesStatuses.Length == 0 ||
            (item is Series { Status: { } status } && source.SeriesStatuses.Contains(status))) &&
        ((source.HasSubtitles is null && source.Is3D is null) ||
            (item is Video video &&
                (source.HasSubtitles is not { } subtitles || video.HasSubtitles == subtitles) &&
                (source.Is3D is not { } is3D || video.Is3D == is3D)));

    private (Guid[] Roots, string? LibraryName) FindScope(Guid? parentId, CancellationToken cancellationToken)
    {
        string? libraryName = null;
        var roots = new HashSet<Guid>();
        var found = false;
        foreach (var entry in library.GetVirtualFolders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.LibraryOptions?.Enabled == false)
            {
                continue;
            }
            var resolved = Guid.TryParse(entry.ItemId, out var id) && id != Guid.Empty;
            if (parentId is { } selected ? !resolved || id != selected : entry.Locations.Length == 0)
            {
                continue;
            }
            if (!resolved || library.GetItemById(id) is not CollectionFolder folder ||
                (folder.PhysicalFolderIds.Length == 0 && entry.Locations.Length != 0))
            {
                throw new InvalidOperationException("A configured Jellyfin library isn't indexed yet.");
            }
            found = true;
            if (parentId is not null)
            {
                libraryName = entry.Name;
            }
            var physicalFolders = new List<Folder>();
            foreach (var root in folder.PhysicalFolderIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (root == Guid.Empty || library.GetItemById(root) is not Folder { Path: not null } physicalFolder)
                {
                    throw new InvalidOperationException("A configured Jellyfin library path could not be resolved.");
                }
                physicalFolders.Add(physicalFolder);
            }
            foreach (var location in entry.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matchingRoots = physicalFolders.Where(physical => fileSystem.AreEqual(physical.Path, location)).ToArray();
                if (matchingRoots.Length == 0)
                {
                    throw new InvalidOperationException("A configured Jellyfin library path could not be resolved.");
                }
                roots.UnionWith(matchingRoots.Select(physical => physical.Id));
            }
        }
        if (parentId is not null && !found)
        {
            throw new InvalidOperationException("parentId does not identify an enabled, configured Jellyfin library.");
        }
        return (roots.ToArray(), libraryName);
    }
}

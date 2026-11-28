namespace Jellyfin.Plugin.MagicCollections.Sync;

public sealed record LibraryMedia(Guid Id, MediaIdentity Identity, MovieTitleYear? MovieTitleYear = null);

public sealed record TaggedCollection(Guid Id, IReadOnlyCollection<Guid> MemberIds);

public interface ICollectionLibrary
{
    IEnumerable<LibraryMedia> GetMedia();
    TaggedCollection? FindByTags(string urlTag, string markerTag, string? serviceTag = null);
    Task<Guid> CreateAsync(string name, string? overview, params string[] tags);
    Task ReplaceTagAsync(Guid id, string oldTag, string newTag, CancellationToken cancellationToken);
    Task AddMembersAsync(Guid id, IReadOnlyCollection<Guid> memberIds);
    Task RemoveMembersAsync(Guid id, IReadOnlyCollection<Guid> memberIds);
    Task EnsureOverviewAsync(Guid id, string overview, CancellationToken cancellationToken);
    Task EnsurePosterAsync(Guid id, IReadOnlyList<Uri> faviconUrls, Uri? listImageUrl, CancellationToken cancellationToken);
}

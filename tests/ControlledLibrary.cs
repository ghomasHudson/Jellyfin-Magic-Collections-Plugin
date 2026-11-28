using Jellyfin.Plugin.MagicCollections.Sync;

namespace MagicCollections.Tests;

internal sealed class ControlledLibrary : ICollectionLibrary
{
    public sealed record ManagedCollection(Guid Id, string Name, List<string> Tags, List<Guid> Members)
    {
        public string? Overview { get; set; }
        public string? PosterTitle { get; set; }
        public IReadOnlyList<Uri> PosterFaviconUrls { get; set; } = [];
        public Uri? PosterListImageUrl { get; set; }
    }

    public List<LibraryMedia> Media { get; } = [];
    public List<ManagedCollection> ManagedCollections { get; } = [];

    public IEnumerable<LibraryMedia> GetMedia() => Media;
    public TaggedCollection? FindByTags(string urlTag, string markerTag, string? serviceTag = null) =>
        ManagedCollections.SingleOrDefault(c => c.Tags.Contains(urlTag) && c.Tags.Contains(markerTag) &&
            (serviceTag is null || c.Tags.Contains(serviceTag))) is { } c
            ? new(c.Id, c.Members.ToArray()) : null;

    public Task<Guid> CreateAsync(string name, params string[] tags) =>
        ((ICollectionLibrary)this).CreateAsync(name, null, tags);

    Task<Guid> ICollectionLibrary.CreateAsync(string name, string? overview, params string[] tags)
    {
        var collection = new ManagedCollection(Guid.NewGuid(), name, [.. tags], [])
        {
            Overview = string.IsNullOrWhiteSpace(overview) ? null : overview
        };
        ManagedCollections.Add(collection);
        return Task.FromResult(collection.Id);
    }

    public Task AddMembersAsync(Guid id, IReadOnlyCollection<Guid> memberIds)
    {
        ManagedCollections.Single(c => c.Id == id).Members.AddRange(memberIds);
        return Task.CompletedTask;
    }

    public Task RemoveMembersAsync(Guid id, IReadOnlyCollection<Guid> memberIds)
    {
        ManagedCollections.Single(c => c.Id == id).Members.RemoveAll(memberIds.Contains);
        return Task.CompletedTask;
    }

    public Task ReplaceTagAsync(Guid id, string oldTag, string newTag, CancellationToken cancellationToken)
    {
        var tags = ManagedCollections.Single(c => c.Id == id).Tags;
        tags.RemoveAll(tag => tag == oldTag || tag == newTag);
        tags.Add(newTag);
        return Task.CompletedTask;
    }

    public Task EnsureOverviewAsync(Guid id, string overview, CancellationToken cancellationToken)
    {
        var collection = ManagedCollections.Single(c => c.Id == id);
        if (string.IsNullOrWhiteSpace(collection.Overview))
        {
            collection.Overview = overview;
        }

        return Task.CompletedTask;
    }

    public Task EnsurePosterAsync(Guid id, IReadOnlyList<Uri> faviconUrls, Uri? listImageUrl, CancellationToken cancellationToken)
    {
        var collection = ManagedCollections.Single(c => c.Id == id);
        if (collection.PosterTitle is null)
        {
            collection.PosterTitle = collection.Name;
            collection.PosterFaviconUrls = faviconUrls;
            collection.PosterListImageUrl = listImageUrl;
        }

        return Task.CompletedTask;
    }
}

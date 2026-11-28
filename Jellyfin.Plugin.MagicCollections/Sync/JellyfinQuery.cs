using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MagicCollections.Sync;

internal sealed class JellyfinQuery
{
    private const string Prefix = "jellyfin/";
    private const int MaxTitleLength = 80;
    private const int MaxOverviewLength = 600;
    private readonly List<JellyfinNativeFilter> _nativeFilters = [];
    private string? _studioTitle;

    public double? MinCriticRating { get; private set; }
    public string[] Studios { get; private set; } = [];
    public BaseItemKind[] ItemTypes { get; private set; } = [BaseItemKind.Movie, BaseItemKind.Series];
    public SeriesStatus[] SeriesStatuses { get; private set; } = [];
    public Guid? ParentId { get; private set; }
    public int? Limit { get; private set; }
    public bool Random { get; private set; }
    public bool? HasSubtitles { get; private set; }
    public bool? Is3D { get; private set; }
    private string? Name { get; set; }
    private string? Description { get; set; }

    public (string Title, string Overview) CollectionMetadata(string? libraryName)
    {
        if (Name is not null && Description is not null)
        {
            return (Name, Description);
        }

        var filters = _nativeFilters.OrderBy(filter => filter.Key, StringComparer.Ordinal)
            .Select(filter => (filter.Title, filter.Detail)).ToList();
        void Add(string title, string? detail = null) => filters.Add((title, detail ?? title));
        if (_studioTitle is not null) Add(_studioTitle);
        if (MinCriticRating is { } score)
        {
            Add(FormattableString.Invariant($"Critic Rating {score}+"),
                FormattableString.Invariant($"Critic rating of at least {score} out of 100"));
        }
        if (SeriesStatuses.Length > 0) Add(string.Join(" or ", SeriesStatuses) + " Series");
        if (HasSubtitles is { } subtitles) Add(subtitles ? "With Subtitles" : "No Subtitles");
        if (Is3D is { } is3D) Add(is3D ? "3D" : "Not 3D");

        var parts = filters.Select(filter => filter.Title).ToList();
        var media = ItemTypes.Length == 2 ? "Movies & Series" : ItemTypes[0] == BaseItemKind.Movie ? "Movies" : "Series";
        libraryName = string.IsNullOrWhiteSpace(libraryName) ? null : libraryName.Trim();
        if (ItemTypes.Length == 1 && SeriesStatuses.Length == 0) parts.Add(media);
        if (libraryName is not null) parts.Add($"Library: {libraryName}");
        if (parts.Count == 0) parts.Add(media);
        if (Limit is { } limit)
        {
            var count = limit.ToString(CultureInfo.InvariantCulture);
            parts.Add(Random ? $"Up to {count} Random Picks" : $"Up to {count}");
        }
        var title = Name ?? Shorten(string.Join(" · ", parts), MaxTitleLength);

        var scope = libraryName is not null ? $"the “{libraryName}” library" :
            ParentId is not null ? "the selected Jellyfin library" : "your enabled Jellyfin libraries";
        var mediaDescription = ItemTypes.Length == 2 ? "movies and series" : media.ToLowerInvariant();
        var overview = $"Automatically collects {mediaDescription} from {scope}.";
        if (Limit is { } maximum)
        {
            var count = maximum.ToString(CultureInfo.InvariantCulture);
            overview += Random
                ? $" Each sync picks up to {count} matches at random."
                : $" Includes up to {count} matches.";
        }
        if (filters.Count > 0)
        {
            overview += $" Filters: {string.Join("; ", filters.Select(filter => filter.Detail))}.";
        }
        return (title, Description ?? Shorten(overview, MaxOverviewLength));
    }

    private static string Shorten(string text, int maxLength)
    {
        var value = new StringInfo(text);
        return value.LengthInTextElements <= maxLength
            ? text
            : value.SubstringByTextElements(0, maxLength - 1).TrimEnd(' ', '·') + "…";
    }

    public void ApplyNativeFilters(InternalItemsQuery query)
    {
        foreach (var filter in _nativeFilters)
        {
            filter.Apply(query);
        }
    }

    // A null result is not a local source; a local source has either a query or a syntax error.
    public static (JellyfinQuery? Query, string? Error)? ParseSource(string? source) =>
        source?.Trim() is { } trimmed && trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? Parse(trimmed)
            : null;

    public string CanonicalSource
    {
        get
        {
            // Only selection parameters contribute; fixed key order and omitted defaults identify equivalents.
            var pairs = new SortedDictionary<string, string>(StringComparer.Ordinal);
            void Add(string key, string value) => pairs.Add(key, Uri.EscapeDataString(value));
            if (HasSubtitles is { } hasSubtitles) Add("hassubtitles", hasSubtitles ? "true" : "false");
            if (ItemTypes.Length != 2) Add("includeitemtypes", ItemTypes[0].ToString());
            if (Is3D is { } is3D) Add("is3d", is3D ? "true" : "false");
            if (Limit is { } limit) Add("limit", limit.ToString(CultureInfo.InvariantCulture));
            if (MinCriticRating is { } rating) Add("mincriticrating", rating.ToString(CultureInfo.InvariantCulture));
            if (ParentId is { } parent) Add("parentid", parent.ToString("N"));
            if (SeriesStatuses.Length > 0) pairs.Add("seriesstatus", JellyfinQueryValues.EncodeList(SeriesStatuses.Select(status => status.ToString())));
            if (Random) Add("sortby", "Random");
            if (Studios.Length > 0) pairs.Add("studios", JellyfinQueryValues.EncodeList(Studios));
            foreach (var filter in _nativeFilters)
            {
                pairs.Add(filter.Key, filter.CanonicalValue);
            }
            return Prefix + string.Join('&', pairs.Select(pair => $"{pair.Key}={pair.Value}"));
        }
    }

    private static (JellyfinQuery? Query, string? Error) Parse(string source)
    {
        var query = new JellyfinQuery();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in source[Prefix.Length..].Split('&'))
        {
            var pair = parameter.Split('=', 2);
            if (pair.Length != 2 || pair[0].Length == 0)
            {
                return (null, "Expected jellyfin/Key=Value with parameters separated by '&'.");
            }

            var key = pair[0].ToLowerInvariant();
            if (!keys.Add(key))
            {
                return (null, "A parameter is specified more than once.");
            }

            var encoded = pair[1];
            if (JellyfinNativeFilters.Supports(key))
            {
                if (!JellyfinNativeFilters.TryParse(key, encoded, out var filter))
                {
                    return (null, $"Invalid value for '{key}'.");
                }
                query._nativeFilters.Add(filter);
                continue;
            }

            if (JellyfinQueryValues.DecodeValue(encoded) is not { Length: > 0 } value)
            {
                return (null, "Each parameter needs a non-empty, URL-encoded value.");
            }

            switch (key)
            {
                case "mincriticrating":
                    if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rating) ||
                        rating is not (>= 0 and <= 100))
                    {
                        return (null, "minCriticRating must be a number between 0 and 100.");
                    }
                    query.MinCriticRating = rating;
                    break;
                case "studios":
                    if (JellyfinQueryValues.DecodeList(encoded) is not { } names)
                    {
                        return (null, "studios must contain non-empty names.");
                    }
                    var studios = names.Select(name => (Name: name, Key: JellyfinQueryValues.CleanMetadataName(name)))
                        .DistinctBy(studio => studio.Key).OrderBy(studio => studio.Key, StringComparer.Ordinal).ToArray();
                    query.Studios = studios.Select(studio => studio.Key).ToArray();
                    if (query.Studios.Any(string.IsNullOrWhiteSpace))
                    {
                        return (null, "studios must contain non-empty names.");
                    }
                    query._studioTitle = "From " + string.Join(" or ", studios.Select(studio => studio.Name));
                    break;
                case "includeitemtypes":
                    if (JellyfinQueryValues.DecodeList(encoded) is not { } typeNames)
                    {
                        return (null, "includeItemTypes supports only Movie and Series.");
                    }
                    var types = new HashSet<BaseItemKind>();
                    foreach (var type in typeNames)
                    {
                        if (type.Equals("Movie", StringComparison.OrdinalIgnoreCase)) types.Add(BaseItemKind.Movie);
                        else if (type.Equals("Series", StringComparison.OrdinalIgnoreCase)) types.Add(BaseItemKind.Series);
                        else return (null, "includeItemTypes supports only Movie and Series.");
                    }
                    query.ItemTypes = types.Order().ToArray();
                    break;
                case "seriesstatus":
                    if (JellyfinQueryValues.DecodeList(encoded) is not { } statusNames)
                    {
                        return (null, "seriesStatus supports only Continuing, Ended and Unreleased.");
                    }
                    var statuses = new HashSet<SeriesStatus>();
                    foreach (var status in statusNames)
                    {
                        if (status.Equals("Continuing", StringComparison.OrdinalIgnoreCase)) statuses.Add(SeriesStatus.Continuing);
                        else if (status.Equals("Ended", StringComparison.OrdinalIgnoreCase)) statuses.Add(SeriesStatus.Ended);
                        else if (status.Equals("Unreleased", StringComparison.OrdinalIgnoreCase)) statuses.Add(SeriesStatus.Unreleased);
                        else return (null, "seriesStatus supports only Continuing, Ended and Unreleased.");
                    }
                    query.SeriesStatuses = statuses.Order().ToArray();
                    break;
                case "hassubtitles":
                case "is3d":
                    if (!bool.TryParse(value, out var flag))
                    {
                        return (null, $"'{key}' must be true or false.");
                    }
                    if (key == "hassubtitles") query.HasSubtitles = flag;
                    else query.Is3D = flag;
                    break;
                case "parentid":
                    if (!Guid.TryParse(value, out var parent) || parent == Guid.Empty)
                    {
                        return (null, "parentId must be a non-empty Jellyfin library ID (GUID).");
                    }
                    query.ParentId = parent;
                    break;
                case "limit":
                    if (!JellyfinQueryValues.TryPositiveInt(value, out var limit))
                    {
                        return (null, "limit must be a positive whole number.");
                    }
                    query.Limit = limit;
                    break;
                case "sortby":
                    if (!value.Equals("Random", StringComparison.OrdinalIgnoreCase))
                    {
                        return (null, "sortBy supports only Random.");
                    }
                    query.Random = true;
                    break;
                case "list_name":
                    query.Name = value;
                    break;
                case "list_desc":
                    query.Description = value;
                    break;
                default:
                    return (null, "An unsupported parameter was specified.");
            }
        }

        if (query.Random && query.Limit is null)
        {
            return (null, "sortBy=Random requires a limit.");
        }

        return (query, null);
    }
}

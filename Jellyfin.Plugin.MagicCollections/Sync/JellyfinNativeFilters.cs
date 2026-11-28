using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MagicCollections.Sync;

internal static class JellyfinNativeFilters
{
    private const int MaxValueLength = 4096;
    private const int MaxListItems = 100;
    private static readonly string[] IsoDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mm'Z'",
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];

    // Ordinary /Items predicates that can be applied directly to the 10.11.5 item query.
    private static readonly Dictionary<string, FilterParser> Parsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["genres"] = MetadataNames("", (query, names) => query.Genres = names, detailPrefix: "Genre: "),
        ["tags"] = MetadataNames("Tagged ", (query, names) => query.Tags = names),
        ["years"] = List<int>(JellyfinQueryValues.TryPositiveInt, year => year.ToString(CultureInfo.InvariantCulture),
            (query, years) => query.Years = years, detailPrefix: "Production year: "),
        ["person"] = Parse<string>(TryText, text => text, (query, name) => query.Person = name, name => $"With {name}"),
        ["searchterm"] = Parse<string>(TrySearchTerm, NormalizeSearchTerm,
            (query, term) => query.SearchTerm = NormalizeSearchTerm(term), term => $"Title Search: {term}"),
        ["mincommunityrating"] = Parse<double>(TryRating, FormatRating,
            (query, rating) => query.MinCommunityRating = rating, rating => $"Community Rating {FormatRating(rating)}+"),
        ["minpremieredate"] = Date("Premiered From", (query, instant) => query.MinPremiereDate = instant),
        ["maxpremieredate"] = Date("Premiered Through", (query, instant) => query.MaxPremiereDate = instant),
        ["mindatelastsaved"] = Date("Last Saved Since", (query, instant) => query.MinDateLastSaved = instant),
        ["officialratings"] = List<string>(TryText, text => text,
            (query, ratings) => query.OfficialRatings = ratings, "Rated ", allowEncodedPipe: true),
        ["hasoverview"] = Boolean("With a Description", "No Description", (query, value) => query.HasOverview = value),
        ["hasimdbid"] = Boolean("With an IMDb ID", "No IMDb ID", (query, value) => query.HasImdbId = value),
        ["hastmdbid"] = Boolean("With a TMDB ID", "No TMDB ID", (query, value) => query.HasTmdbId = value),
        ["hastvdbid"] = Boolean("With a TVDB ID", "No TVDB ID", (query, value) => query.HasTvdbId = value),
        ["hasofficialrating"] = Boolean("With a Content Rating", "No Content Rating", (query, value) => query.HasOfficialRating = value),
        ["islocked"] = Boolean("Locked Metadata", "Unlocked Metadata", (query, value) => query.IsLocked = value),
        ["namestartswith"] = SortName("Sort Title Starts With", (query, name) => query.NameStartsWith = name),
        ["namestartswithorgreater"] = SortName("Sort Title From", (query, name) => query.NameStartsWithOrGreater = name),
        ["namelessthan"] = SortName("Sort Title Before", (query, name) => query.NameLessThan = name),
        ["imagetypes"] = EnumList<ImageType>("Image Type: ", (query, types) => query.ImageTypes = types),
        ["videotypes"] = EnumList<VideoType>("Video Format: ", (query, types) => query.VideoTypes = types),
        ["minwidth"] = Dimension("Min Width", (query, pixels) => query.MinWidth = pixels),
        ["maxwidth"] = Dimension("Max Width", (query, pixels) => query.MaxWidth = pixels),
        ["minheight"] = Dimension("Min Height", (query, pixels) => query.MinHeight = pixels),
        ["maxheight"] = Dimension("Max Height", (query, pixels) => query.MaxHeight = pixels)
    };

    private delegate JellyfinNativeFilter? FilterParser(string key, string encoded);
    private delegate bool ValueParser<T>(string value, out T result);

    internal static bool Supports(string key) => Parsers.ContainsKey(key);

    internal static bool TryParse(string key, string encoded, [NotNullWhen(true)] out JellyfinNativeFilter? filter)
    {
        // One encoded UTF-8 byte takes three characters per escape, up to four bytes per code point.
        filter = encoded.Length <= MaxValueLength * 12 && Parsers.TryGetValue(key, out var parser)
            ? parser(key.ToLowerInvariant(), encoded)
            : null;
        return filter is not null;
    }

    private static FilterParser Parse<T>(ValueParser<T> parse, Func<T, string> format, Action<InternalItemsQuery, T> apply,
        Func<T, string> title) =>
        (key, encoded) => JellyfinQueryValues.DecodeValue(encoded) is { Length: > 0 and <= MaxValueLength } value &&
            parse(value, out var result)
            ? new JellyfinNativeFilter(key, Uri.EscapeDataString(format(result)), title(result), query => apply(query, result))
            : null;

    private static FilterParser MetadataNames(string prefix, Action<InternalItemsQuery, string[]> apply, string? detailPrefix = null) =>
        List<string>(TryMetadataName, JellyfinQueryValues.CleanMetadataName, apply, prefix, display: name => name, detailPrefix: detailPrefix);

    private static bool TryMetadataName(string name, out string result)
    {
        result = name;
        return !string.IsNullOrWhiteSpace(JellyfinQueryValues.CleanMetadataName(name));
    }

    private static bool TrySearchTerm(string value, out string result)
    {
        result = value;
        return !string.IsNullOrWhiteSpace(JellyfinQueryValues.CleanMetadataName(value));
    }

    // Jellyfin also matches OriginalTitle, which does not remove accents.
    private static string NormalizeSearchTerm(string value) =>
        value.All(char.IsAscii) ? value.ToLowerInvariant() : value;

    private static string FormatRating(double rating) => rating.ToString("R", CultureInfo.InvariantCulture);

    private static bool TryRating(string value, out double rating) =>
        double.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture,
            out rating) && rating is >= 0 and <= 10;

    private static FilterParser Date(string label, Action<InternalItemsQuery, DateTime> apply) =>
        Parse<DateTimeOffset>(TryDate, date => date.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            (query, date) => apply(query, date.UtcDateTime),
            date => $"{label} {date.ToString(date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss.FFFFFFF 'UTC'",
                CultureInfo.InvariantCulture)}");

    private static bool TryDate(string value, out DateTimeOffset date) =>
        DateTimeOffset.TryParseExact(value, IsoDateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);

    private static FilterParser Boolean(string present, string absent, Action<InternalItemsQuery, bool> apply) =>
        Parse<bool>(bool.TryParse, value => value ? "true" : "false", apply, value => value ? present : absent);

    private static FilterParser Dimension(string label, Action<InternalItemsQuery, int> apply) =>
        Parse<int>(JellyfinQueryValues.TryPositiveInt, pixels => pixels.ToString(CultureInfo.InvariantCulture),
            apply, pixels => FormattableString.Invariant($"{label}: {pixels}px"));

    private static FilterParser SortName(string label, Action<InternalItemsQuery, string> apply) =>
        Parse<string>(TryText, name => name.ToLowerInvariant(), (query, name) => apply(query, name.ToLowerInvariant()),
            name => $"{label} {name}");

    private static FilterParser EnumList<T>(string prefix, Action<InternalItemsQuery, T[]> apply) where T : struct, Enum
    {
        var names = Enum.GetValues<T>().ToDictionary(value => value.ToString(), StringComparer.OrdinalIgnoreCase);
        return List<T>(names.TryGetValue, value => value.ToString(), apply, prefix);
    }

    private static FilterParser List<T>(ValueParser<T> parse, Func<T, string> format, Action<InternalItemsQuery, T[]> apply,
        string prefix = "", bool allowEncodedPipe = false, Func<T, string>? display = null, string? detailPrefix = null) =>
        (key, encoded) =>
        {
            if (JellyfinQueryValues.DecodeList(encoded, MaxListItems, allowEncodedPipe) is not { } parts ||
                parts.Sum(part => part.Length) + parts.Length - 1 > MaxValueLength)
            {
                return null;
            }

            var values = new SortedDictionary<string, T>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                if (!parse(part, out var item))
                {
                    return null;
                }
                values.TryAdd(format(item), item);
            }

            var canonical = JellyfinQueryValues.EncodeList(values.Keys);
            var items = values.Values.ToArray();
            var names = string.Join(" or ", items.Select(display ?? format));
            // Keep the parsed value independent of each mutable query.
            return new JellyfinNativeFilter(key, canonical, prefix + names, query => apply(query, items.ToArray()),
                detailPrefix is null ? null : detailPrefix + names);
        };

    private static bool TryText(string value, out string result)
    {
        result = value;
        return value.Length > 0;
    }
}

internal sealed class JellyfinNativeFilter(string key, string canonicalValue, string title, Action<InternalItemsQuery> apply,
    string? detail = null)
{
    internal string Key { get; } = key;
    internal string CanonicalValue { get; } = canonicalValue;
    internal string Title { get; } = title;
    internal string Detail { get; } = detail ?? title;

    internal void Apply(InternalItemsQuery query) => apply(query);
}

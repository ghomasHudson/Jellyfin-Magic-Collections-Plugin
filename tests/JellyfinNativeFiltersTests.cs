using Jellyfin.Plugin.MagicCollections.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Xunit;

namespace MagicCollections.Tests;

public sealed class JellyfinNativeFiltersTests
{
    [Fact]
    public void Community_rating_uses_the_native_threshold_and_a_canonical_identity()
    {
        var (filter, query) = ApplyFilter("MinCOMMUNITYRating", "07.500");

        Assert.Equal("mincommunityrating", filter.Key);
        Assert.Equal("7.5", filter.CanonicalValue);
        Assert.Equal(7.5, query.MinCommunityRating);
        Assert.Null(query.MinCriticRating);
    }

    [Theory]
    [InlineData("minPremiereDate", "MinPremiereDate")]
    [InlineData("maxPremiereDate", "MaxPremiereDate")]
    [InlineData("minDateLastSaved", "MinDateLastSaved")]
    public void Date_bounds_use_the_same_UTC_instant_regardless_of_offset(string key, string property)
    {
        var (dateOnly, firstQuery) = ApplyFilter(key, "2025-01-02");
        var (withOffset, secondQuery) = ApplyFilter(key, "2025-01-01T19%3A00%3A00.000-05%3A00");

        Assert.Equal("2025-01-02T00%3A00%3A00.0000000Z", dateOnly.CanonicalValue);
        Assert.Equal(dateOnly.CanonicalValue, withOffset.CanonicalValue);
        var expected = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        AssertDateBound(firstQuery, property, expected);
        AssertDateBound(secondQuery, property, expected);
    }

    [Fact]
    public void Official_ratings_are_an_exact_any_of_list_that_preserves_case()
    {
        var (filter, query) = ApplyFilter("officialRatings", "TV-MA+%7C+PG-13,TV-MA+%7C+pg-13");
        var (reordered, _) = ApplyFilter("officialratings", "pg-13,TV-MA,PG-13");

        Assert.Equal("PG-13,TV-MA,pg-13", filter.CanonicalValue);
        Assert.Equal(filter.CanonicalValue, reordered.CanonicalValue);
        Assert.Equal(["PG-13", "TV-MA", "pg-13"], query.OfficialRatings);
        Assert.Null(query.MinParentalRating);
        Assert.Null(query.MaxParentalRating);
    }

    [Theory]
    [InlineData("hasOverview", "TRUE", true)]
    [InlineData("hasImdbId", "false", false)]
    [InlineData("hasTmdbId", "True", true)]
    [InlineData("hasTvdbId", "False", false)]
    [InlineData("hasOfficialRating", "false", false)]
    [InlineData("isLocked", "true", true)]
    public void Both_boolean_values_are_explicit_query_predicates(string key, string input, bool expected)
    {
        var (filter, query) = ApplyFilter(key, input);
        var actual = key switch
        {
            "hasOverview" => query.HasOverview,
            "hasImdbId" => query.HasImdbId,
            "hasTmdbId" => query.HasTmdbId,
            "hasTvdbId" => query.HasTvdbId,
            "hasOfficialRating" => query.HasOfficialRating,
            "isLocked" => query.IsLocked,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };

        Assert.Equal(expected ? "true" : "false", filter.CanonicalValue);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Name_prefix_and_bounds_use_the_same_case_folding_as_native_sorted_names()
    {
        var (prefix, prefixQuery) = ApplyFilter("nameStartsWith", "%C3%89p");
        var (lower, lowerQuery) = ApplyFilter("nameStartsWithOrGreater", "B");
        var (upper, upperQuery) = ApplyFilter("nameLessThan", "P");

        Assert.Equal("%C3%A9p", prefix.CanonicalValue);
        Assert.Equal("ép", prefixQuery.NameStartsWith);
        Assert.Equal("b", lower.CanonicalValue);
        Assert.Equal("b", lowerQuery.NameStartsWithOrGreater);
        Assert.Equal("p", upper.CanonicalValue);
        Assert.Equal("p", upperQuery.NameLessThan);
        Assert.Null(prefixQuery.SearchTerm);
    }

    [Fact]
    public void Image_and_video_type_lists_accept_public_enum_names_case_insensitively()
    {
        var (images, imageQuery) = ApplyFilter("imageTypes", "primary,+BACKDROP,+Primary");
        var (videos, videoQuery) = ApplyFilter("videoTypes", "Videofile,+bluray");

        Assert.Equal("Backdrop,Primary", images.CanonicalValue);
        Assert.Equal([ImageType.Backdrop, ImageType.Primary], imageQuery.ImageTypes);
        Assert.Equal("BluRay,VideoFile", videos.CanonicalValue);
        Assert.Equal([VideoType.BluRay, VideoType.VideoFile], videoQuery.VideoTypes);
    }

    [Theory]
    [InlineData("minWidth", "01920", 1920)]
    [InlineData("maxWidth", "4096", 4096)]
    [InlineData("minHeight", "00720", 720)]
    [InlineData("maxHeight", "2160", 2160)]
    public void Dimensions_select_the_items_own_stored_pixel_bounds(string key, string input, int expected)
    {
        var (filter, query) = ApplyFilter(key, input);
        var actual = key switch
        {
            "minWidth" => query.MinWidth,
            "maxWidth" => query.MaxWidth,
            "minHeight" => query.MinHeight,
            "maxHeight" => query.MaxHeight,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };

        Assert.Equal(input.TrimStart('0'), filter.CanonicalValue);
        Assert.Equal(expected, actual);
        Assert.Null(query.IsHD);
        Assert.Null(query.Is4K);
    }

    [Theory]
    [InlineData("minCommunityRating", "-1")]
    [InlineData("minCommunityRating", "10.1")]
    [InlineData("minCommunityRating", "NaN")]
    [InlineData("minCommunityRating", "Infinity")]
    [InlineData("minCommunityRating", "7,5")]
    [InlineData("minPremiereDate", "02%2F01%2F2025")]
    [InlineData("maxPremiereDate", "2025-02-29")]
    [InlineData("minDateLastSaved", "2025-01-02T09%3A00%3A00")]
    [InlineData("minDateLastSaved", "2025-01-02T09%3A00%3A00%2B15%3A00")]
    [InlineData("officialRatings", "PG-13%7C,TV-MA")]
    [InlineData("hasOverview", "1")]
    [InlineData("hasImdbId", "True%00")]
    [InlineData("isLocked", "yes")]
    [InlineData("nameStartsWith", "+")]
    [InlineData("imageTypes", "Primary,0")]
    [InlineData("imageTypes", "Unknown")]
    [InlineData("videoTypes", "1")]
    [InlineData("minWidth", "0")]
    [InlineData("maxWidth", "2147483648")]
    [InlineData("minHeight", "720.0")]
    [InlineData("maxHeight", "-720")]
    public void Malformed_values_are_distinct_from_unsupported_keys(string key, string value)
    {
        Assert.True(JellyfinNativeFilters.Supports(key));
        Assert.False(JellyfinNativeFilters.TryParse(key, value, out var filter));
        Assert.Null(filter);
    }

    [Theory]
    [InlineData("parentId")]
    [InlineData("includeItemTypes")]
    [InlineData("userId")]
    [InlineData("isFavorite")]
    [InlineData("isPlayed")]
    [InlineData("hasParentalRating")]
    [InlineData("minOfficialRating")]
    [InlineData("isHD")]
    [InlineData("is4K")]
    [InlineData("hasTrailer")]
    [InlineData("personTypes")]
    [InlineData("enableImageTypes")]
    [InlineData("ancestorIds")]
    public void Unsafe_or_unverified_parameters_and_base_options_are_not_registered(string key)
    {
        Assert.False(JellyfinNativeFilters.Supports(key));
        Assert.False(JellyfinNativeFilters.TryParse(key, "true", out var filter));
        Assert.Null(filter);
    }

    [Fact]
    public void Oversized_filters_are_rejected_instead_of_truncated()
    {
        Assert.False(JellyfinNativeFilters.TryParse("nameStartsWith", new string('a', 4097), out _));
        Assert.False(JellyfinNativeFilters.TryParse("imageTypes", string.Join(',', Enumerable.Repeat("Primary", 101)), out _));
    }

    [Fact]
    public void Canonical_values_are_valid_input_and_reapplying_a_filter_uses_its_original_values()
    {
        var (date, _) = ApplyFilter("maxPremiereDate", "2025-01-02T03%3A00%2B03%3A00");
        var (rating, _) = ApplyFilter("minCommunityRating", "0.0000001");
        var (list, firstQuery) = ApplyFilter("videoTypes", "ISO,BluRay");
        firstQuery.VideoTypes[0] = VideoType.Dvd;

        foreach (var filter in new[] { date, rating, list })
        {
            Assert.True(JellyfinNativeFilters.TryParse(filter.Key, filter.CanonicalValue, out var reparsed));
            Assert.Equal(filter.CanonicalValue, reparsed.CanonicalValue);
        }

        var secondQuery = new InternalItemsQuery();
        list.Apply(secondQuery);
        Assert.Equal([VideoType.BluRay, VideoType.Iso], secondQuery.VideoTypes);
    }

    private static void AssertDateBound(InternalItemsQuery query, string property, DateTime expected)
    {
        var bound = property switch
        {
            "MinPremiereDate" => query.MinPremiereDate,
            "MaxPremiereDate" => query.MaxPremiereDate,
            "MinDateLastSaved" => query.MinDateLastSaved,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
        Assert.Equal(expected, bound);
        Assert.Equal(DateTimeKind.Utc, bound?.Kind);
        Assert.Null(query.MinDateLastSavedForUser);
    }

    private static (JellyfinNativeFilter Filter, InternalItemsQuery Query) ApplyFilter(string key, string value)
    {
        Assert.True(JellyfinNativeFilters.Supports(key));
        Assert.True(JellyfinNativeFilters.TryParse(key, value, out var filter));
        Assert.NotNull(filter);
        var query = new InternalItemsQuery();
        filter.Apply(query);
        return (filter, query);
    }
}

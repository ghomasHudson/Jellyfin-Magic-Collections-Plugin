using System.Reflection;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MagicCollections.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MagicCollections.Tests;

public sealed class JellyfinQuerySyncTests
{
    [Fact]
    public async Task Genre_and_year_select_exact_movies_and_whole_series_in_the_configured_library()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (selected, root) = index.AddLibrary();
        var (_, otherRoot) = index.AddLibrary();
        var (_, disabledRoot) = index.AddLibrary(enabled: false);
        var noProvider = index.Add(new Movie(), root);
        var series = index.Add(new Series(), root);
        var matchingCopy = index.Add(new Movie { ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt1234567" } }, root);
        var wrongGenreCopy = index.Add(new Movie { ProviderIds = matchingCopy.ProviderIds }, root, genres: ["Comedy"]);
        var outsideCopy = index.Add(new Movie { ProviderIds = matchingCopy.ProviderIds }, otherRoot);
        index.Add(new Movie(), disabledRoot);
        index.Add(new Movie(), root, year: 2023);
        index.Add(new Episode(), root);
        index.Add(new Movie { IsVirtualItem = true }, root);
        index.Add(new Movie { IsPlaceHolder = true }, root);
        index.Add(new Movie { ChannelId = Guid.NewGuid() }, root);
        index.Add(new Movie { IsShortcut = true }, root);
        index.Add(new Movie { ExtraType = ExtraType.Trailer }, root);
        library.Media.Add(new(wrongGenreCopy.Id, new(SourceMediaType.Movie, wrongGenreCopy.ProviderIds)));
        library.Media.Add(new(outsideCopy.Id, new(SourceMediaType.Movie, outsideCopy.ProviderIds)));
        var sync = services.GetRequiredService<CollectionSync>();

        await sync.RunAsync(
            [$"jellyfin/genres=Mystery&years=2025&parentId={selected:N}&list_name=Mystery%202025"],
            Assert.Fail, CancellationToken.None);

        var collection = Assert.Single(library.ManagedCollections);
        Assert.Equal("Mystery 2025", collection.Name);
        Assert.Contains("Jellyfin", collection.Tags);
        Assert.Equal(new[] { noProvider.Id, series.Id, matchingCopy.Id }.Order(), collection.Members.Order());

        await sync.RunAsync(["jellyfin/genres=Mystery&years=2025"], Assert.Fail, CancellationToken.None);

        var global = Assert.Single(library.ManagedCollections, c => c.Id != collection.Id);
        Assert.Equal("Mystery · 2025", global.Name);
        Assert.Equal("Automatically collects movies and series from your enabled Jellyfin libraries. Filters: Genre: Mystery; Production year: 2025.",
            global.Overview);
        Assert.Equal(new[] { noProvider.Id, series.Id, matchingCopy.Id, outsideCopy.Id }.Order(), global.Members.Order());
    }

    [Theory]
    [InlineData("mincriticRating=09.0", "Critic Rating 9+")]
    [InlineData("limit=3&includeItemTypes=Movie&sortBy=Random", "Movies · Up to 3 Random Picks")]
    [InlineData("years=2025&genres=Mystery,Science+Fiction", "Mystery or Science Fiction · 2025")]
    [InlineData("studios=Caf%C3%A9%2C+Inc.", "From Café, Inc.")]
    [InlineData("searchTerm=Ast%C3%A9rix", "Title Search: Astérix")]
    [InlineData("searchTerm=Asterix", "Title Search: Asterix")]
    [InlineData("hasImdbId=false&minCommunityRating=7.50", "No IMDb ID · Community Rating 7.5+")]
    [InlineData("seriesStatus=ended&includeItemTypes=Series", "Ended Series")]
    [InlineData("limit=2", "Movies & Series · Up to 2")]
    public void Collection_titles_describe_the_query(string filters, string title)
    {
        var query = JellyfinQuery.ParseSource("jellyfin/" + filters)?.Query;
        Assert.NotNull(query);
        Assert.Equal(title, query.CollectionMetadata(libraryName: null).Title);
    }

    [Fact]
    public void Long_generated_metadata_is_shortened_without_splitting_unicode_text_elements()
    {
        var tag = string.Concat(Enumerable.Repeat("🎬", 700));
        var query = JellyfinQuery.ParseSource("jellyfin/tags=" + Uri.EscapeDataString(tag))?.Query;
        Assert.NotNull(query);

        var (title, overview) = query.CollectionMetadata(libraryName: null);
        Assert.Equal("Tagged " + string.Concat(Enumerable.Repeat("🎬", 72)) + "…", title);
        const string prefix = "Automatically collects movies and series from your enabled Jellyfin libraries. Filters: Tagged ";
        Assert.Equal(prefix + string.Concat(Enumerable.Repeat("🎬", 599 - prefix.Length)) + "…", overview);
    }

    [Fact]
    public void Custom_title_does_not_suppress_generated_overview_and_custom_overview_does_not_suppress_generated_title()
    {
        const string source = "jellyfin/mincriticRating=9&sortBy=Random&limit=3&includeItemTypes=Movie";
        var named = JellyfinQuery.ParseSource(source + "&list_name=Movie%20Night")?.Query;
        var described = JellyfinQuery.ParseSource(source + "&list_desc=My%20description")?.Query;
        Assert.NotNull(named);
        Assert.NotNull(described);
        Assert.Equal(named.CanonicalSource, described.CanonicalSource);
        Assert.Equal(("Movie Night", "Automatically collects movies from your enabled Jellyfin libraries. " +
            "Each sync picks up to 3 matches at random. Filters: Critic rating of at least 9 out of 100."),
            named.CollectionMetadata(libraryName: null));
        Assert.Equal(("Critic Rating 9+ · Movies · Up to 3 Random Picks", "My description"),
            described.CollectionMetadata(libraryName: null));
    }

    [Fact]
    public async Task New_scoped_collections_use_the_library_name_without_renaming_existing_collections()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (scope, root) = index.AddLibrary(name: "Family Movies");
        var item = index.Add(new Movie(), root);
        var sync = services.GetRequiredService<CollectionSync>();
        var scoped = $"jellyfin/parentId={scope:N}";
        var global = "jellyfin/genres=Mystery";
        await sync.RunAsync([global + "&list_name=Jellyfin%20library%20query"], Assert.Fail, CancellationToken.None);
        var existing = Assert.Single(library.ManagedCollections);
        existing.Overview = null; // Simulate a collection created before overviews were generated.

        await sync.RunAsync([scoped, global], Assert.Fail, CancellationToken.None);

        var created = Assert.Single(library.ManagedCollections, collection => collection.Id != existing.Id);
        Assert.Equal("Library: Family Movies", created.Name);
        Assert.Equal("Automatically collects movies and series from the “Family Movies” library.", created.Overview);
        Assert.Equal([item.Id], created.Members);
        Assert.Equal("Jellyfin library query", existing.Name);
        Assert.EndsWith("Filters: Genre: Mystery.", existing.Overview);
        Assert.Equal([item.Id], existing.Members);
    }

    [Fact]
    public async Task Movies_of_the_day_limits_the_eligible_movies_after_checking_critic_ratings()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        index.Add(new Movie { CriticRating = 8 }, root);
        index.Add(new Movie(), root);
        index.Add(new Series { CriticRating = 100 }, root);
        var eligible = new[]
        {
            index.Add(new Movie { CriticRating = 9 }, root).Id,
            index.Add(new Movie { CriticRating = 20 }, root).Id,
            index.Add(new Movie { CriticRating = 30 }, root).Id,
            index.Add(new Movie { CriticRating = 40 }, root).Id
        };

        await services.GetRequiredService<CollectionSync>().RunAsync(
            ["jellyfin/mincriticRating=9&sortBy=Random&limit=3&includeItemTypes=Movie&list_name=Movies%20of%20the%20Day&list_desc=3%20highly-rated%20movies"],
            Assert.Fail, CancellationToken.None);

        var collection = Assert.Single(library.ManagedCollections);
        Assert.Equal("Movies of the Day", collection.Name);
        Assert.Equal("3 highly-rated movies", collection.Overview);
        Assert.Equal(3, collection.Members.Distinct().Count());
        Assert.All(collection.Members, id => Assert.Contains(id, eligible));
    }

    [Fact]
    public async Task Community_rating_and_overview_narrow_the_scoped_movies_before_the_limit()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (scope, root) = index.AddLibrary();
        var (_, otherRoot) = index.AddLibrary();

        var eligible = new Movie { Name = "Eligible", CommunityRating = 7.5f, CriticRating = 30, Overview = "Summary" };
        index.Add(new Movie { CommunityRating = 7.5f, CriticRating = 8, Overview = "Summary" }, root);
        index.Add(new Movie { CommunityRating = 7.5f, CriticRating = 30 }, root);
        index.Add(new Movie { CommunityRating = 5, CriticRating = 30, Overview = "Summary" }, root);
        index.Add(new Movie { CommunityRating = 8, CriticRating = 30, Overview = "Summary" }, root, year: 2024);
        index.Add(new Series { CommunityRating = 8, CriticRating = 30, Overview = "Summary" }, root);
        index.Add(new Movie { CommunityRating = 8, CriticRating = 30, Overview = "Summary" }, otherRoot);
        index.Add(eligible, root);

        await services.GetRequiredService<CollectionSync>().RunAsync(
            [$"jellyfin/parentId={scope:N}&includeItemTypes=Movie&years=2025&minCommunityRating=7.5&hasOverview=true&minCriticRating=9&limit=1"],
            Assert.Fail, CancellationToken.None, reportJellyfinFailure: Assert.Fail);

        Assert.Equal([eligible.Id], Assert.Single(library.ManagedCollections).Members);
    }

    [Fact]
    public async Task Series_status_selects_whole_series_with_a_matching_indexed_status_before_limit()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (scope, root) = index.AddLibrary();
        var (_, otherRoot) = index.AddLibrary();
        index.Add(new Movie { Name = "Ended" }, root);
        index.Add(new Series { Name = "Ended", Status = SeriesStatus.Unreleased }, root);
        index.Add(new Series { Name = "Continuing" }, root);
        index.Add(new Series { Status = SeriesStatus.Ended }, otherRoot);
        var ended = index.Add(new Series { Name = "Finished", Status = SeriesStatus.Ended }, root);
        var continuing = index.Add(new Series { Name = "In progress", Status = SeriesStatus.Continuing }, root);
        var sync = services.GetRequiredService<CollectionSync>();

        await sync.RunAsync([$"jellyfin/parentId={scope:N}&seriesStatus=ended,Continuing&limit=2&list_desc=Original"],
            Assert.Fail, CancellationToken.None);
        var collection = Assert.Single(library.ManagedCollections);
        Assert.Equal(new[] { ended.Id, continuing.Id }.Order(), collection.Members.Order());

        await sync.RunAsync([$"jellyfin/seriesStatus=CONTINUING,Ended,ended&parentId={scope:N}&limit=2&list_desc=Updated"],
            Assert.Fail, CancellationToken.None);
        Assert.Same(collection, Assert.Single(library.ManagedCollections));
        Assert.Equal("Original", collection.Overview);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Subtitles_match_the_movie_itself_not_episodes_or_non_video_items(bool hasSubtitles)
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        index.Add(new Series(), root);
        index.Add(new Episode { HasSubtitles = true }, root);
        var withSubtitles = index.Add(new Movie { HasSubtitles = true }, root);
        var withoutSubtitles = index.Add(new Movie(), root);

        await services.GetRequiredService<CollectionSync>().RunAsync(
            [$"jellyfin/hasSubtitles={hasSubtitles}&limit=1"], Assert.Fail, CancellationToken.None);

        Assert.Equal([hasSubtitles ? withSubtitles.Id : withoutSubtitles.Id],
            Assert.Single(library.ManagedCollections).Members);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Three_dimensional_selection_checks_the_movie_video_format(bool is3D)
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        index.Add(new Series(), root);
        var flat = index.Add(new Movie { Name = "3D in title only" }, root);
        var stereo = index.Add(new Movie { Video3DFormat = Video3DFormat.FullSideBySide }, root);

        await services.GetRequiredService<CollectionSync>().RunAsync(
            [$"jellyfin/is3D={is3D}&limit=1"], Assert.Fail, CancellationToken.None);

        Assert.Equal([is3D ? stereo.Id : flat.Id], Assert.Single(library.ManagedCollections).Members);
    }

    [Fact]
    public async Task Tags_person_title_and_known_studios_narrow_a_query_without_creating_studios()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        index.AddStudio("Studio A");
        var match = index.Add(new Movie { Name = "Asterix and Obelix", Tags = ["film noir"], Studios = ["Studio A"] }, root);
        var withoutTag = index.Add(new Movie { Name = "Asterix Returns", Studios = ["Studio A"] }, root);
        var differentStudio = index.Add(new Movie { Name = "Asterix Again", Tags = ["film noir"], Studios = ["Studio C"] }, root);
        var differentTitle = index.Add(new Movie { Name = "Tintin", Tags = ["film noir"], Studios = ["Studio A"] }, root);
        index.Add(new Movie { Name = "Asterix Extra", Tags = ["film noir"], Studios = ["Studio A"] }, root);
        foreach (var movie in new[] { match, withoutTag, differentStudio, differentTitle })
        {
            index.Credit(movie, "Wes Anderson");
        }
        var sync = services.GetRequiredService<CollectionSync>();

        await sync.RunAsync(
            ["jellyfin/tags=film%20noir&person=Wes%20Anderson&searchTerm=Asterix&studios=Studio%20A,Unknown"],
            Assert.Fail, CancellationToken.None);

        Assert.Equal([match.Id], Assert.Single(library.ManagedCollections).Members);
        await sync.RunAsync(["jellyfin/studios=Unknown"], Assert.Fail, CancellationToken.None);
        Assert.Single(library.ManagedCollections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_studio_ID_parameter_is_rejected_even_when_studio_names_are_provided(bool namesFirst)
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        index.AddStudio("Studio A");
        var otherStudio = index.AddStudio("Studio B");
        index.Add(new Movie { Studios = ["Studio A"] }, root);
        index.Add(new Movie { Studios = ["Studio B"] }, root);

        var names = "studios=Studio%20A";
        var ids = $"studioIds={otherStudio:N}";
        var source = namesFirst ? $"jellyfin/{names}&{ids}" : $"jellyfin/{ids}&{names}";
        var reports = new List<string>();
        await services.GetRequiredService<CollectionSync>().RunAsync([source],
            Assert.Fail, CancellationToken.None, reportJellyfinFailure: reports.Add);

        Assert.Contains("unsupported parameter", Assert.Single(reports));
        Assert.Empty(library.ManagedCollections);
    }

    [Fact]
    public async Task Equivalent_queries_reuse_membership_while_only_the_first_entry_supplies_metadata_per_run()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (scope, root) = index.AddLibrary();
        index.Add(new Movie { Name = "Asterix", CriticRating = 20 }, root, genres: ["Mystery"]);
        var sync = services.GetRequiredService<CollectionSync>();
        var first = $"jellyfin/genres=Mystery,Science%20Fiction&searchTerm=ASTERIX&years=2025,2024&parentId={scope:D}&minCriticRating=009.0&list_name=Original";
        var equivalent = $"Jellyfin/YeaRS=02024,2025,2024&GENRES=SCIENCE+FICTION,%6dystery&searchTerm=asterix&parentid={scope:N}&includeItemTypes=Series,movie&MinCriticRating=9&list_name=New&list_desc=Updated";

        await sync.RunAsync([first, equivalent], Assert.Fail, CancellationToken.None);

        var collection = Assert.Single(library.ManagedCollections);
        Assert.Equal("Original", collection.Name);
        var overview = collection.Overview;
        Assert.Contains("Genre: Mystery or Science Fiction", overview);

        await sync.RunAsync([equivalent], Assert.Fail, CancellationToken.None);

        Assert.Same(collection, Assert.Single(library.ManagedCollections));
        Assert.Equal("Original", collection.Name);
        Assert.Equal(overview, collection.Overview);
    }

    [Theory]
    [InlineData("genres")]
    [InlineData("tags")]
    [InlineData("studios")]
    public async Task Accent_equivalent_metadata_names_reuse_the_same_collection(string filter)
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        index.AddStudio("Café");
        var movie = index.Add(new Movie { Tags = ["Café"], Studios = ["Café"] }, root, genres: ["Café"]);
        var sync = services.GetRequiredService<CollectionSync>();

        await sync.RunAsync([$"jellyfin/{filter}=Caf%C3%A9"], Assert.Fail, CancellationToken.None);
        var collection = Assert.Single(library.ManagedCollections);
        Assert.Contains("Café", collection.Overview);
        var overview = collection.Overview;
        await sync.RunAsync([$"jellyfin/{filter}=CAFE&list_desc=Updated"], Assert.Fail, CancellationToken.None);

        Assert.Same(collection, Assert.Single(library.ManagedCollections));
        Assert.Equal(overview, collection.Overview);
        Assert.Equal([movie.Id], collection.Members);
    }

    [Theory]
    [InlineData("genres")]
    [InlineData("tags")]
    [InlineData("studios")]
    public async Task Encoded_commas_in_metadata_names_do_not_split_or_share_identity_with_separate_names(string filter)
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        index.AddStudio("A,B");
        index.AddStudio("A");
        var literal = index.Add(new Movie { Tags = ["A,B"], Studios = ["A,B"] }, root, genres: ["A,B"]);
        var separate = index.Add(new Movie { Tags = ["A"], Studios = ["A"] }, root, genres: ["A"]);
        var sync = services.GetRequiredService<CollectionSync>();

        await sync.RunAsync(
            [$"jellyfin/{filter}=A%2CB&list_name=Literal", $"jellyfin/{filter}=A,B&list_name=List"],
            Assert.Fail, CancellationToken.None);

        Assert.Equal([literal.Id], Assert.Single(library.ManagedCollections, c => c.Name == "Literal").Members);
        Assert.Equal([separate.Id], Assert.Single(library.ManagedCollections, c => c.Name == "List").Members);
    }

    [Fact]
    public async Task Equivalent_extended_filters_reuse_the_same_collection()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        var movie = index.Add(new Movie { CommunityRating = 8, Overview = "Summary" }, root);
        var sync = services.GetRequiredService<CollectionSync>();
        var first = "jellyfin/years=2025&hasOverview=TRUE&minCommunityRating=07.50&list_desc=Original";
        var equivalent = "JELLYFIN/MINCOMMUNITYRATING=7.5&years=02025&HASoverview=%74rue&list_desc=Updated";

        await sync.RunAsync([first], Assert.Fail, CancellationToken.None, reportJellyfinFailure: Assert.Fail);
        var collection = Assert.Single(library.ManagedCollections);
        Assert.Equal("Original", collection.Overview);

        await sync.RunAsync([equivalent], Assert.Fail, CancellationToken.None, reportJellyfinFailure: Assert.Fail);

        Assert.Same(collection, Assert.Single(library.ManagedCollections));
        Assert.Equal([movie.Id], collection.Members);
        Assert.Equal("Original", collection.Overview);
    }

    [Theory]
    [InlineData("minCommunityRating", "not-a-rating")]
    [InlineData("hasOverview", "yes")]
    [InlineData("minPremiereDate", "2025-02-30")]
    public async Task Malformed_extended_values_are_reported_without_changing_managed_collections(string filter, string value)
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        var movie = index.Add(new Movie(), root);
        var sync = services.GetRequiredService<CollectionSync>();
        await sync.RunAsync(["jellyfin/genres=Mystery&list_desc=Original"], Assert.Fail, CancellationToken.None);
        var collection = Assert.Single(library.ManagedCollections);
        index.Add(new Movie(), root);

        var reports = new List<string>();
        await sync.RunAsync([$"jellyfin/genres=Mystery&{filter}={value}&list_desc=Changed"],
            Assert.Fail, CancellationToken.None, reportJellyfinFailure: reports.Add);

        var report = Assert.Single(reports);
        Assert.StartsWith("Invalid Jellyfin library query:", report);
        Assert.Contains(filter, report, StringComparison.OrdinalIgnoreCase);
        Assert.Same(collection, Assert.Single(library.ManagedCollections));
        Assert.Equal([movie.Id], collection.Members);
        Assert.Equal("Original", collection.Overview);
    }

    [Fact]
    public async Task Invalid_or_failed_queries_preserve_membership_while_a_successful_empty_read_clears_it()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (scope, root) = index.AddLibrary();
        var mystery = index.Add(new Movie(), root);
        var comedy = index.Add(new Movie(), root, genres: ["Comedy"]);
        var sync = services.GetRequiredService<CollectionSync>();
        var source = $"jellyfin/genres=Mystery&parentId={scope:N}";
        var reports = new List<string>();
        await sync.RunAsync([source], Assert.Fail, CancellationToken.None, reportJellyfinFailure: reports.Add);
        var collection = Assert.Single(library.ManagedCollections);
        Assert.Equal([mystery.Id], collection.Members);

        index.FailNextRead = true;
        await sync.RunAsync(["jellyfin/genres=Mystery&userId=secret", source, "jellyfin/genres=Comedy"],
            Assert.Fail, CancellationToken.None, reportJellyfinFailure: reports.Add);

        Assert.Collection(reports,
            invalid => Assert.StartsWith("Invalid Jellyfin library query:", invalid),
            failed => Assert.Contains("Index temporarily unavailable", failed));
        Assert.DoesNotContain(reports, report => report.Contains("secret", StringComparison.Ordinal));
        Assert.Equal([mystery.Id], collection.Members);
        Assert.Equal([comedy.Id], Assert.Single(library.ManagedCollections, c => c.Id != collection.Id).Members);

        reports.Clear();
        index.SetLibraryEnabled(scope, false);
        await sync.RunAsync([source, $"jellyfin/parentId={root:N}"], Assert.Fail, CancellationToken.None,
            reportJellyfinFailure: reports.Add);
        Assert.Equal(2, reports.Count);
        Assert.All(reports, report => Assert.Contains("does not identify an enabled, configured Jellyfin library", report));
        Assert.Equal([mystery.Id], collection.Members);

        reports.Clear();
        index.SetLibraryEnabled(scope, true);
        mystery.Genres = ["Thriller"];
        await sync.RunAsync([source, "jellyfin/genres=Unmatched"], Assert.Fail, CancellationToken.None,
            reportJellyfinFailure: reports.Add);
        Assert.Empty(reports);
        Assert.Empty(collection.Members);
        Assert.Equal(2, library.ManagedCollections.Count);
    }

    [Fact]
    public async Task Missing_physical_roots_fail_without_falling_back_to_unscoped_items()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (scope, root) = index.AddLibrary();
        var movie = index.Add(new Movie(), root);
        var sync = services.GetRequiredService<CollectionSync>();
        var source = $"jellyfin/parentId={scope:N}";
        await sync.RunAsync([source], Assert.Fail, CancellationToken.None);
        var collection = Assert.Single(library.ManagedCollections);

        index.SetUnindexed(scope);
        var reports = new List<string>();
        await sync.RunAsync([source, "jellyfin/genres=Mystery"], Assert.Fail, CancellationToken.None,
            reportJellyfinFailure: reports.Add);

        Assert.Equal(2, reports.Count);
        Assert.All(reports, report => Assert.Contains("isn't indexed yet", report));
        Assert.Equal([movie.Id], collection.Members);
        Assert.Single(library.ManagedCollections);
    }

    [Fact]
    public async Task A_partially_unresolved_library_preserves_scoped_and_global_membership()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (scope, root) = index.AddLibrary();
        var secondRoot = index.AddLibraryPath(scope, "/other-media/");
        var first = index.Add(new Movie(), root);
        var second = index.Add(new Movie(), secondRoot);
        var sync = services.GetRequiredService<CollectionSync>();
        var sources = new[] { $"jellyfin/parentId={scope:N}", "jellyfin/genres=Mystery" };
        await sync.RunAsync(sources, Assert.Fail, CancellationToken.None);
        Assert.Equal(2, library.ManagedCollections.Count);

        index.SetUnindexed(scope, secondRoot);
        var reports = new List<string>();
        await sync.RunAsync(sources, Assert.Fail, CancellationToken.None, reportJellyfinFailure: reports.Add);

        Assert.Equal(2, reports.Count);
        Assert.All(reports, report => Assert.Contains("path could not be resolved", report));
        Assert.All(library.ManagedCollections, collection =>
            Assert.Equal(new[] { first.Id, second.Id }, collection.Members.Order()));
    }

    [Fact]
    public async Task Limit_without_random_has_stable_membership_even_when_Jellyfin_changes_its_result_order()
    {
        var (library, index, provider) = CreateLibrary();
        using var services = provider;
        var (_, root) = index.AddLibrary();
        var first = index.Add(new Movie(), root);
        var second = index.Add(new Movie(), root);
        var third = index.Add(new Series(), root);
        var expected = new[] { first.Id, second.Id, third.Id }.Order().Take(2);
        var sync = services.GetRequiredService<CollectionSync>();

        await sync.RunAsync(["jellyfin/limit=2"], Assert.Fail, CancellationToken.None);
        var collection = Assert.Single(library.ManagedCollections);
        Assert.Equal(expected, collection.Members.Order());

        index.ReverseOrder();
        await sync.RunAsync(["jellyfin/limit=2"], Assert.Fail, CancellationToken.None);
        Assert.Equal(expected, collection.Members.Order());
    }

    private static (ControlledLibrary Collections, LibraryIndex Index, ServiceProvider Services) CreateLibrary()
    {
        var manager = DispatchProxy.Create<ILibraryManager, LibraryIndex>();
        var index = (LibraryIndex)(object)manager;
        var collections = new ControlledLibrary();
        var services = PluginTestServices.BuildProvider(configure: services =>
        {
            services.AddSingleton(manager);
            services.AddSingleton(DispatchProxy.Create<IFileSystem, FileSystemPaths>());
            services.AddSingleton<ICollectionLibrary>(collections);
        });
        return (collections, index, services);
    }

    public class FileSystemPaths : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name switch
            {
                nameof(IFileSystem.AreEqual) => Path.TrimEndingDirectorySeparator((string)args![0]!) ==
                    Path.TrimEndingDirectorySeparator((string)args[1]!),
                _ => throw new NotSupportedException(method?.Name)
            };
    }

    public class LibraryIndex : DispatchProxy
    {
        private readonly List<VirtualFolderInfo> _libraries = [];
        private readonly Dictionary<Guid, BaseItem> _folders = [];
        private readonly List<(BaseItem Item, Guid Root)> _media = [];
        private readonly Dictionary<string, Guid> _studios = new();
        private readonly Dictionary<Guid, string> _credits = [];

        public bool FailNextRead { get; set; }
        public Guid AddStudio(string name)
        {
            var id = Guid.NewGuid();
            _studios.Add(MetadataKey(name), id);
            return id;
        }
        public void Credit(BaseItem item, string person) => _credits.Add(item.Id, person);
        public void ReverseOrder() => _media.Reverse();
        public void SetLibraryEnabled(Guid id, bool enabled) =>
            _libraries.Single(folder => folder.ItemId == id.ToString("N")).LibraryOptions.Enabled = enabled;
        public void SetUnindexed(Guid id) => ((CollectionFolder)_folders[id]).PhysicalFolderIds = [];
        public void SetUnindexed(Guid id, Guid root)
        {
            var folder = (CollectionFolder)_folders[id];
            folder.PhysicalFolderIds = folder.PhysicalFolderIds.Where(candidate => candidate != root).ToArray();
        }

        public Guid AddLibraryPath(Guid id, string path)
        {
            var root = Guid.NewGuid();
            _folders[root] = new Folder { Id = root, Path = Path.TrimEndingDirectorySeparator(path) };
            var library = _libraries.Single(folder => folder.ItemId == id.ToString("N"));
            library.Locations = [.. library.Locations, path];
            var folder = (CollectionFolder)_folders[id];
            folder.PhysicalFolderIds = [.. folder.PhysicalFolderIds, root];
            return root;
        }

        public (Guid Id, Guid Root) AddLibrary(bool enabled = true, string name = "Fixture Library")
        {
            var id = Guid.NewGuid();
            var root = Guid.NewGuid();
            _libraries.Add(new() { Name = name, ItemId = id.ToString("N"), Locations = ["/media"], LibraryOptions = new() { Enabled = enabled } });
            _folders[id] = new CollectionFolder { Id = id, PhysicalFolderIds = [root] };
            _folders[root] = new Folder { Id = root, Path = "/media" };
            return (id, root);
        }

        public T Add<T>(T item, Guid root, int year = 2025, string[]? genres = null) where T : BaseItem
        {
            // Predictable IDs make fixtures testing limited membership independent of chance.
            item.Id = new Guid(_media.Count + 1, 0, 0, new byte[8]);
            item.Path = $"/media/{item.Id:N}";
            item.Genres = genres ?? ["Mystery"];
            item.ProductionYear = year;
            _media.Add((item, root));
            return item;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name switch
            {
                nameof(ILibraryManager.GetVirtualFolders) => _libraries,
                nameof(ILibraryManager.GetItemById) => _folders.GetValueOrDefault((Guid)args![0]!),
                nameof(ILibraryManager.GetItemList) => Query((InternalItemsQuery)args![0]!),
                nameof(ILibraryManager.GetItemIds) => FindStudio((InternalItemsQuery)args![0]!),
                _ => throw new NotSupportedException(method?.Name)
            };

        private Guid[] FindStudio(InternalItemsQuery query)
        {
            Assert.Equal([BaseItemKind.Studio], query.IncludeItemTypes);
            return _studios.TryGetValue(MetadataKey(query.Name!), out var id) ? [id] : [];
        }

        private BaseItem[] Query(InternalItemsQuery query)
        {
            if (FailNextRead)
            {
                FailNextRead = false;
                throw new IOException("Index temporarily unavailable");
            }
            Assert.Null(query.User);
            Assert.False(query.GroupByPresentationUniqueKey);
            Assert.False(query.GroupBySeriesPresentationUniqueKey);
            Assert.NotEmpty(query.AncestorIds);
            return _media
                .Where(entry => query.AncestorIds.Contains(entry.Root))
                .Select(entry => entry.Item)
                .Where(item => query.IncludeItemTypes.Contains(item.GetBaseItemKind()))
                .Where(item => query.Genres.Count == 0 || item.Genres.Select(MetadataKey).Intersect(query.Genres.Select(MetadataKey)).Any())
                .Where(item => query.Tags.Length == 0 || item.Tags.Select(MetadataKey).Intersect(query.Tags.Select(MetadataKey)).Any())
                .Where(item => query.Person is null || _credits.GetValueOrDefault(item.Id) == query.Person)
                .Where(item => query.SearchTerm is null || item.Name.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase))
                .Where(item => query.StudioIds.Length == 0 ||
                    item.Studios.Any(name => query.StudioIds.Contains(_studios.GetValueOrDefault(MetadataKey(name)))))
                .Where(item => query.Years.Length == 0 || query.Years.Contains(item.ProductionYear!.Value))
                .Where(item => query.IsVirtualItem is null || query.IsVirtualItem == item.IsVirtualItem)
                .Where(item => query.IsPlaceHolder is null || query.IsPlaceHolder == (item is Video { IsPlaceHolder: true }))
                .Where(item => query.MinCommunityRating is not { } rating || item.CommunityRating >= rating)
                .Where(item => query.HasOverview is not { } hasOverview || hasOverview == !string.IsNullOrEmpty(item.Overview))
                .Take(query.Limit ?? int.MaxValue)
                .ToArray();
        }

        // The fixture only needs the index's case folding and é/e equivalence.
        private static string MetadataKey(string name) => name.ToLowerInvariant().Replace('é', 'e');
    }
}

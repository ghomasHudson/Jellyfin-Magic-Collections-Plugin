# Jellyfin library queries

Add a query under **Source list URLs** in the Magic Collections settings. Try `jellyfin/genres=Mystery&years=2025` to collect mystery movies and series from 2025. Paste the query into the settings field rather than opening it in a browser.

## Examples

- Find titles with a critic score of at least 9: `jellyfin/mincriticRating=9`
- Pick up to three qualifying **Movies of the Day** on each sync: `jellyfin/minCriticRating=9&limit=3&sortBy=Random&includeItemTypes=Movie&list_name=Movies%20of%20the%20Day&list_desc=3%20highly-rated%20movies`
- Collect film-noir mysteries: `jellyfin/tags=film%20noir&genres=Mystery&list_name=Film-Noir%20Mysteries`
- Find movies that credit Wes Anderson: `jellyfin/person=Wes%20Anderson&includeItemTypes=Movie&list_name=Wes%20Anderson%20Collection`
- Search for Asterix in titles: `jellyfin/searchTerm=Asterix&list_name=Asterix%20%26%20Obelix`
- Collect movies from either of two Marvel studios: `jellyfin/studios=Marvel%20Studios,Marvel%20Entertainment&includeItemTypes=Movie&list_name=Studio%3A%20Marvel`
- Select 2025 movies from one library by substituting your library ID: `jellyfin/years=2025&includeItemTypes=Movie&parentId=814be7ca-fba3-4f5a-9850-001540d06df3`
- Find 2025 movies with a community score of at least 7.5 and a description: `jellyfin/years=2025&minCommunityRating=7.5&hasOverview=true&includeItemTypes=Movie`
- Collect PG-13 or TV-14 titles that premiered in 2025: `jellyfin/minPremiereDate=2025-01-01&maxPremiereDate=2025-12-31T23%3A59%3A59.9999999Z&officialRatings=PG-13,TV-14`
- Collect finished series: `jellyfin/seriesStatus=Ended`

## Query format

Start with `jellyfin/` and separate `Key=Value` pairs with `&`. Keys are case-insensitive and can appear only once. URL-encode spaces and punctuation inside values, especially `&` (`%26`) and `+` (`%2B`). Both `%20` and `+` stand for a space.

### Filters

Use commas to separate list values. Encode a comma within a metadata name as `%2C`. Each filter must match; within a list, any listed value can match.

#### Titles and metadata

| Key | What it does |
| --- | --- |
| `genres`, `tags`, `studios` | Match any of the comma-separated metadata names. |
| `person` | Match one credited name as stored by Jellyfin, including case; any credited role counts. |
| `searchTerm` | Use Jellyfin's title search, where `%` and `_` can match multiple titles as wildcards. |
| `nameStartsWith`, `nameStartsWithOrGreater`, `nameLessThan` | Match a prefix, an inclusive lower bound, or an exclusive upper bound against Jellyfin's **sort title**. This title can differ from the one you see in your library. |

#### Metadata checks

| Key | What it does |
| --- | --- |
| `hasOverview`, `hasOfficialRating` | Use `true` to require a description or content rating, respectively. Use `false` to find items missing it. |
| `hasImdbId`, `hasTmdbId`, `hasTvdbId` | Use `true` to require that provider's ID or `false` to require its absence. |
| `isLocked` | Use `true` for locked metadata or `false` for unlocked metadata. |

#### Dates

| Key | What it does |
| --- | --- |
| `years` | Match any of the listed production years, not the year an item was added or an episode aired. |
| `minPremiereDate`, `maxPremiereDate` | Set inclusive bounds on the item's premiere date, not its production year or the date it joined your library. Items without a premiere date don't match. |
| `minDateLastSaved` | Match items Jellyfin last saved on or after this date, such as after a metadata edit. This is not the date added or a user's watch date. |

Use `YYYY-MM-DD` for midnight Coordinated Universal Time (UTC). A date-only upper bound includes midnight at the start of that date, not the rest of the day.

For a specific time, give an ISO 8601 timestamp with a time zone. Encode `:` as `%3A` and encode the `+` of a positive offset as `%2B`. For example, `2025-01-01T00%3A00%3A00Z` and `2025-01-01T01%3A00%3A00%2B01%3A00` refer to the same instant.

#### Ratings

| Key | What it does |
| --- | --- |
| `minCriticRating` | Set a minimum critic score from 0 to 100, not a community rating. Items without a critic score don't match. |
| `minCommunityRating` | Set a minimum community score from 0 to 10, for example `7.5`. Unrated items don't match. |
| `officialRatings` | Match exact content rating labels, including case. Separate labels with commas (`PG-13,TV-MA`) or encoded pipes (`PG-13%7CTV-MA`). |

#### Images, video and series

| Key | What it does |
| --- | --- |
| `imageTypes` | Require at least one of the listed image types on the item. Use names such as `Primary` or `Backdrop`, not numbers. |
| `videoTypes` | Match any of `VideoFile`, `Iso`, `Dvd`, or `BluRay` for the item's video format, not its codec or file extension. |
| `minWidth`, `maxWidth`, `minHeight`, `maxHeight` | Set inclusive bounds, in positive whole pixels, on the item's indexed dimensions. These do not inspect individual episodes or select a video version for playback. |
| `seriesStatus` | Match whole series with any of the listed statuses: `Continuing`, `Ended`, `Unreleased`. Movies and series without a status don't match. |
| `hasSubtitles`, `is3D` | Use `true` or `false` to match movies with or without indexed subtitles or a 3D video format, respectively. Neither value matches whole series based on their episodes. |
| `includeItemTypes` | Use `Movie`, `Series`, or both (the default). Individual episodes and seasons are never included. |

### Scope and selection

| Key | What it does |
| --- | --- |
| `parentId` | Restrict the search to one enabled top-level library. Use its Jellyfin library ID, not a library name, folder path, or a user ID. Jellyfin lists these IDs as `ItemId` in its library settings API (`GET /Library/VirtualFolders`). |
| `limit` | Select up to this many distinct items after all filters. It must be a positive whole number. With no `sortBy`, the selection is stable and based on item ID. |
| `sortBy` | Only `Random` is supported, and it requires `limit`. It takes a fresh sample on each sync, which can include items from the previous run. It does not set the display order of the collection. |

### Display

| Key | What it does |
| --- | --- |
| `list_name` | Name a new collection. If omitted, the plugin uses the filters and library scope to make a title such as **Mystery · 2025**. To rename an existing collection, edit it in Jellyfin. |
| `list_desc` | Supply an overview for a collection with a blank one. If omitted, the plugin describes the filters, library scope, and any result limit. To change a nonblank overview, edit it in Jellyfin. |

## Library scope and sync behavior

Without `parentId`, the plugin searches the movies and whole series Jellyfin has indexed under all enabled local libraries. It excludes virtual items, placeholders, and links to streams (`.strm` files). It can still include indexed files that are temporarily unreachable.

The query runs server-wide. Jellyfin still controls what each user can access. The plugin does not accept another server, an arbitrary endpoint, a user ID, a password, or an API token.

The result uses the exact Jellyfin items that matched. An item doesn't need an IMDb, TMDB, or other provider ID. Two copies can both be included if both match; one copy never pulls in another just because they share metadata IDs.

Equivalent queries use the same collection even if you change the key order, key case, or the order and repetition of list values. Changing `list_name` or `list_desc` doesn't change which collection the plugin manages. If multiple entries describe the same query, the first wins for that sync. A different filter or library scope is a different source and doesn't replace the old collection.

Invalid filters, invalid library IDs, and failed reads are logged and leave existing membership alone. A successful search with no matches clears an existing managed collection's membership but doesn't create a new empty collection.

# Changelog

## v0.8.0.0 (2026-11-28)

### Feat

- **arr**: detect and test server connections automatically
- sync collections from Sonarr tags
- sync collections from Radarr tags
- **sync**: describe Jellyfin query collections
- **sync**: wire extended Jellyfin query filters
- **sync**: build collections from local Jellyfin queries
- **sync**: add typed Jellyfin item predicates

### Fix

- validate Arr settings and preserve tag collection identity
- **sync**: harden Jellyfin library query handling
- **sync**: preserve nonblank collection overviews

## v0.7.0.0 (2026-11-28)

### Feat

- **sync**: mirror managed collections from source lists

## v0.6.0.0 (2026-11-27)

### Feat

- **trakt**: import the connected account's watchlist
- **trakt**: import recommendations and public smart lists

## v0.5.0.0 (2026-11-27)

### Feat

- **trakt**: sync private lists with PKCE sign-in

### Fix

- **posters**: make icons opt-in and restore Trakt favicon downloads
- **imdb**: remove direct source sync and browser helper
- **trakt**: streamline private list setup and OAuth

## v0.4.2.0 (2026-11-27)

### Fix

- **imdb**: bundle LinuxServer runtime dependencies

## v0.4.1.0 (2026-11-27)

### Fix

- **imdb**: bundle libXext and clarify helper startup failures

## v0.4.0.0 (2026-11-27)

### Feat

- **trakt**: sync public user lists and charts
- **config**: add optional Trakt Client ID override
- **imdb**: add list sync with an on-demand browser helper
- **config**: put verbose logging in a collapsible Debug section
- **config**: add verbose sync logging option
- **config**: make poster settings collapsible
- **posters**: add manual regeneration task
- **posters**: add settings for poster elements
- **config**: warn about unsupported source list URLs

### Fix

- **config**: show Client ID directly in the Trakt panel
- **trakt**: use the official site icon for managed collections (#15)
- **sync**: redact credentials from unsupported URL warnings (#15)
- **config**: sanitize Trakt URLs before saving settings (#15)
- **trakt**: canonicalize public list slugs (#15)
- **trakt**: allow live chart totals to change between pages (#15)
- **release**: include release notes in plugin manifest
- **config**: restore verbose logging label and pad Debug controls
- **config**: remove visible verbose logging text
- **posters**: recheck collection before clearing its poster
- **config**: flag unsupported URLs with a red border
- **config**: keep Save stable during URL validation

### Refactor

- **trakt**: share media mapping and pagination fixtures (#15)
- **posters**: clear posters and let sync regenerate them

## v0.3.0.0 (2026-11-27)

### Feat

- **sync**: tag collections with their source and list URL
- **posters**: use source list artwork as the background
- **posters**: add source favicons to collection posters
- **bfi**: sync editorial lists by movie title and year

### Fix

- **bfi**: include the site favicon on collection posters
- **posters**: skip branded MDBList artwork for backgrounds
- **posters**: normalize artwork before applying an 80% black overlay
- **bfi**: use Open Graph metadata for collection descriptions
- **bfi**: distinguish captioned lists from other articles

## v0.2.3.0 (2026-11-26)

### Feat

- **letterboxd**: move cache clearing to a manual task

## v0.2.2.0 (2026-11-26)

### Feat

- **letterboxd**: cache film IDs until manually cleared

## v0.2.1.0 (2026-11-26)

### Feat

- **sync**: copy source list descriptions to collection overviews (#25)

### Fix

- **sync**: make Letterboxd list trailing slash optional (#26)
- **sync**: read list descriptions without changing source markup (#25)
- **sync**: accept Letterboxd film aliases in public lists (#26)

## v0.2.0.0 (2026-11-26)

### Feat

- **sync**: support Steven Lu Popular Movies feeds
- **sync**: support public Letterboxd lists
- **collections**: generate missing collection posters
- **catalog**: add Magic Collections logo
- **docker**: preconfigure the Jellyfin test sandbox

### Fix

- **sync**: verify Letterboxd membership and media types (#14)
- **sync**: persist Popular Movies polling across restarts
- **collections**: render readable Unicode posters and preserve existing art

## v0.1.1.0 (2026-11-26)

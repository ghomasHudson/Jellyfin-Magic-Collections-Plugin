# Jellyfin Magic Collections

<img width="800" alt="image" src="https://github.com/user-attachments/assets/06681ad8-0ca4-4b78-b423-9fa34639a2ff" />
<br/>
<img width="400" height="188" alt="image" src="https://github.com/user-attachments/assets/4849512f-e9a5-4a41-89c2-ad1a49f0f922" />
<img width="400" height="188" alt="image" src="https://github.com/user-attachments/assets/07281f77-3697-4d52-8369-ff1ccc4e04a3" />


Automatically create and update Jellyfin collections from lists on sites like Letterboxd and Trakt.

New to Magic Collections? [Start with the wiki walkthrough](https://github.com/ghomasHudson/Jellyfin-Magic-Collections-Plugin/wiki/Getting-Started).

## Install

1. Open your Jellyfin server in a browser and sign in with your admin account.
2. Go to **Dashboard → Plugins → Manage Repositories**. If you see a **Repositories** tab instead, open it.
3. Select **New Repository**. For **Repository Name**, enter `Magic Collections`. For **Repository URL**, paste:

   ```text
   https://raw.githubusercontent.com/ghomasHudson/Jellyfin-Magic-Collections-Plugin/main/manifest.json
   ```

4. Select **Add**.
5. Return to **Plugins**. Under **Available**, open **Magic Collections** and select **Install**.
6. On the **Dashboard**, select **Restart**.

## Setup

1. Open **Dashboard → Plugins → Magic Collections** and select **Settings**. Paste [supported list URLs](#supported-list-sources) into the blank fields. Select **Save**.
2. Go to **Dashboard → Scheduled Tasks** and select the play button next to **Sync Magic Collections**. The task also runs daily at 03:00 server time by default.
3. If the task fails, check the Jellyfin server log.

You should now see collections for each of your list URLs.

### Supported List Sources

- Letterboxd - public lists (`https://letterboxd.com/official/list/letterboxds-top-500-films/`)
- mdblist - e.g. [Top Movies of the week](https://mdblist.com/lists/garycrawfordgc/top-movies-of-the-week)
- [Steven Lu Popular movies](https://github.com/sjlu/popular-movies)
- [BFI](https://www.bfi.org.uk/articles/type/lists)
- [Trakt lists and charts](docs/trakt.md) [privacy policy](https://github.com/ghomasHudson/Jellyfin-Magic-Collections-Plugin/wiki/Privacy-Policy)
- [Jellyfin library queries](docs/jellyfin-queries.md)
- [Radarr and Sonarr tags](docs/arr.md)

See [supported list URLs](#supported-list-sources) for more details.

### Not yet supported

Follow these issues for the status of other sources from Jellyfin Auto Collections:

- [Criterion Channel](https://github.com/ghomasHudson/Jellyfin-Magic-Collections-Plugin/issues/18)
- [ListMania](https://github.com/ghomasHudson/Jellyfin-Magic-Collections-Plugin/issues/12)

# Developers

## Build and install manually

The plugin targets Jellyfin Server **10.11.5** and uses its SkiaSharp and HarfBuzz libraries. Correct ordering for mixed left-to-right and right-to-left titles requires the operating system's International Components for Unicode (ICU) library, included in the official Jellyfin container.

Install the **.NET 9 SDK**, version 9.0.200 or later in the 9.0 series. On Linux, the poster tests need ICU, Fontconfig, and fonts with Latin, Arabic, and Japanese glyphs (for example, `fonts-dejavu-core` and `fonts-noto-cjk` on Ubuntu). Run this command from the repository root:

```sh
dotnet test MagicCollections.slnx -c Release
```

This builds the plugin and runs the tests. Stop Jellyfin and create a `MagicCollections` directory under the server’s `plugins` directory. Copy `Jellyfin.Plugin.MagicCollections.dll` and `AngleSharp.dll` from `Jellyfin.Plugin.MagicCollections/bin/Release/net9.0/` and `LICENSE.AngleSharp` from the repository root into that directory, then start Jellyfin again.

## Contributing

Use [Commitizen](https://commitizen-tools.github.io/commitizen/) to write Conventional Commits. Install it with [pipx](https://pipx.pypa.io/), then use `cz commit` instead of `git commit` after staging your changes:

```sh
pipx install 'commitizen==4.20.1'  # one-time setup
git add <files>
cz commit
```

For example: `fix(sync): skip duplicate collection entries`.

GitHub Actions builds and tests every pull request and every push to `main`. It also checks the commits introduced by each pull request without checking the base branch's existing history. For a branch targeting `main`, preview the commit check locally:

```sh
git fetch origin
cz check --rev-range origin/main..HEAD
```

If you squash-merge a pull request, use a Conventional Commit subject for the resulting commit too.

### Publish a release

In GitHub, you can start a release from **Actions → Release plugin → Run workflow**. Run it on `main`. Commitizen reads Conventional Commits to choose the next version when you select `auto`. If you’re publishing the first release and have no eligible commits yet, select `patch`.

The workflow runs `cz bump` to update the project version and `CHANGELOG.md` and create the release commit and tag. For example, Commitizen version `0.1.1` produces the Jellyfin assembly version `0.1.1.0` and tag `v0.1.1.0`. You can preview the changelog with `cz changelog --dry-run`.

You can also prepare the release locally. Start with a clean working tree on `main`:

```sh
git switch main
git pull --ff-only --tags
cz bump --check-consistency
git push --atomic origin main "refs/tags/v$(cz version --project).0"
```

Add `--increment PATCH` to `cz bump` to choose a patch release instead.

The workflow tests the plugin and generates GitHub release notes with `cz changelog`. It adds the new version and download details to `manifest.json` without removing older entries. If publishing fails after the tag reaches GitHub, you can run the workflow on that tag without bumping the version again:

```sh
gh workflow run release.yml --ref v0.1.1.0
```

## Try the plugin in Docker

Run this from the repository root. Docker Compose builds the plugin and starts Jellyfin 10.11.5 in a separate container.

```sh
docker compose up --build -d
```

To keep an existing Auto Collections sandbox and its data, run `docker compose -p autocollections-dev up --build -d`. Use `-p autocollections-dev` with any later Compose commands for that sandbox.

Open <http://localhost:8097>. On a fresh sandbox, Docker completes the setup wizard automatically. Sign in as `admin` with the password `magiccollections-test`. If you're reusing a sandbox, keep using your existing login. The default credentials are for local testing only; don't expose this sandbox to the network.

If port 8097 is in use, set `JELLYFIN_TEST_PORT` to a free port before starting Compose.

The container adds a **Magic Collections Test Movies** library for `/test-media` unless a library already uses that path. It contains zero-byte placeholders for The Matrix, Inception, and Tenet. They can be scanned and matched, but not played. Jellyfin scans them automatically on a fresh sandbox. If you reuse an existing sandbox and the titles don't appear, select **Scan All Libraries** on the dashboard.

1. Choose a public MDBList containing one of the test movies and check that the scanned item has a matching provider ID.
2. Open **Dashboard → Plugins → Magic Collections**. Save the full list page URL and reopen the page to confirm it was saved.
3. Open **Dashboard → Scheduled Tasks → Sync Magic Collections**. Check that it has a daily trigger, then select **Run now**.
4. Open **Collections** in Jellyfin. Check for the matching item and a black poster with the collection name. To include the MDBList favicon on new posters, enable **Poster → Icon** in the plugin settings.
5. Set a custom poster for the collection. Run the task again and confirm it keeps your poster.

After editing the plugin, rerun `docker compose up --build -d` to install the rebuilt plugin.

- Run `docker compose down` to stop Jellyfin and keep your settings.
- Run `docker compose down -v` to stop Jellyfin and delete the sandbox's configuration and cache.

## Add a new site

Implement `ISourceListHandler` to recognize a site's URLs and read its source lists. Register the handler in `ServiceRegistrator`. Handlers do not query the Jellyfin library or write collections.

Return a `SourceList` with the list name, plain-text description, favicon URLs, and list image URL (`ImageUrl`) when available. Use only HTTPS image URLs on the source site or its known asset host.

Put entries with a media type and provider IDs in `ProviderItems`. British Film Institute (BFI) lists are the only exception: put movie titles and release years in `MoviesByTitleYear` and leave `ProviderItems` empty.

`CollectionSync` matches movies and series by media type and at least one shared provider name and ID. BFI lists explicitly use the separate movie title-and-year match described above.

A new collection uses the list name, or the final URL segment with spaces in place of hyphens and underscores. `JellyfinCollectionLibrary` handles Jellyfin queries and writes.

The plugin and settings page are adapted from the [official Jellyfin plugin template](https://github.com/jellyfin/jellyfin-plugin-template/tree/c93225a0a5a76d3843db05b4b5b77fcfc482fba3).

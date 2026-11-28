# Radarr and Sonarr tag collections

Magic Collections can mirror the movies assigned to a Radarr tag or the whole TV series assigned to a Sonarr tag. It only includes titles already in your Jellyfin library. It doesn't download media, modify either server, or combine tags from multiple servers into one collection.

## Add a server

1. In Radarr or Sonarr, copy the API key from **Settings → General → Security**.
2. In Jellyfin, open **Dashboard → Plugins → Magic Collections** and expand **\*arr**.
3. Enter your server's **Base URL** and **API key** in the same row. Include the port and reverse proxy path if your server uses them. Use a distinct base URL for each server. A blank row appears for the next server. To remove a server, clear both fields and save.
4. Select **Test servers** to have Jellyfin check each server's API key, service, and version. The test doesn't read tags, save settings, or change collections.

## Set the source URL

1. Under **Source list URLs**, add the base URL followed by `/` and the exact tag label. URL-encode the label if it contains spaces or other special characters. For example, if your reverse proxy serves Sonarr at `https://media.example.com/sonarr`, use `https://media.example.com/sonarr/family-shows` for the `family-shows` tag.
2. Select **Save**. To sync now, run **Sync Magic Collections** from **Scheduled Tasks**.

The source list URL identifies the tag to Magic Collections. It doesn't have to open a tag page in your browser. Use the same scheme, host, port, and case-sensitive base path as your configured server. Use a hostname or an address without an IPv6 zone identifier. Don't put a username, password, API key, query string, or fragment in either URL. API keys are stored in the Jellyfin plugin's admin settings; protect your Jellyfin configuration and backups. Use HTTPS where possible.

If you reuse a base URL for the other service, Magic Collections treats its tags as new source lists and does not update the old service's collections.

## Supported versions

Use Radarr **4.0.5 or later** or Sonarr **4.0 or later**, with API v3. Sync reports an error for older servers.

Magic Collections matches titles by media type and provider ID. Radarr supplies IMDb and TMDb IDs for movies. Sonarr supplies TVDb, IMDb, and TMDb IDs for whole series when available. Early Sonarr 4 releases don't return TMDb IDs; a TVDb ID alone is enough.

If a tag has no matching titles, sync can empty its managed collection. Sync never deletes media. If Jellyfin can't identify the server or read the tag, it leaves the collection unchanged. Check the Jellyfin log and make sure the base URL and API key are correct.

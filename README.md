# Live-TV Groups for Jellyfin

A Jellyfin plugin that lets every user organize Live TV channels into personal groups, e.g. "Public broadcasters", "Sports" or "Documentaries".

- **Web client:** Live TV gets a **Groups** button. Create groups, pick channels, reorder them via drag & drop and start channels directly.
- **Apps** (Android TV, mobile, …): groups are available under **Channels → Live-TV Gruppen**.
- **Apps without channel support** (e.g. Wholphin): optionally, groups are mirrored as playlists named "Live-TV: &lt;group&gt;". Enable it under Dashboard → Plugins → Live-TV Groups.
- **Program guide (web):** every group has a **Program** tab with a timeline guide (EPG) of its channels. Click a program for details, click a channel to play it.
- **Program guide in TV apps** (Fire TV / Android TV, Wholphin, …): pick a group for the TV guide – in the web client via 📺 on a group, or on the TV under **Channels → Live-TV Gruppen → 📺 Programmführer wählen**. Live TV and the guide of those apps then show only the channels of that group. "Alle Sender" shows all channels again.
- Groups are **per user**. Channels a user may not access (parental control, channel restrictions) stay hidden.

Requires **Jellyfin 12.0**.

## Installation

1. Dashboard → Plugins → Repositories → **+** and add:
   ```
   https://github.com/iEnki/jellyfin-plugin-livetv-groups/releases/latest/download/manifest.json
   ```
2. For the web client integration, also add the **File Transformation** repository:
   ```
   https://www.iamparadox.dev/jellyfin/plugins/manifest.json
   ```
3. Install **Live-TV Groups** and **File Transformation** from the catalog and restart Jellyfin.
4. Check the status under Dashboard → Plugins → Live-TV Groups.

## Notes

- If channel ids change (e.g. the M3U was regenerated and re-imported), channels are matched again by name and number.
- **App channel and playlists:** Jellyfin cannot open live streams through channels the regular Live TV way. The plugin therefore probes the tuner stream and passes it to the server, which remuxes it. Jellyfin's tuner limit does **not** apply here, so keep your provider's connection limit in mind. The stream URL is part of the playback info sent to clients.
- The TV guide filter applies to `GET /LiveTv/Channels` for all apps except the ones listed under "Apps ohne Gruppenfilter" (default: Jellyfin Web). It can be disabled in the plugin settings.
- The web integration depends on the jellyfin-web UI; major Jellyfin updates may require a new plugin version.

## Development

```bash
dotnet test
```

Release: push a tag `vX.Y.Z`. GitHub Actions builds the plugin, creates the release, adds the version to `manifest.json` and attaches the manifest to the release.

License: GPL-3.0

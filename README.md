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

## Features in detail

### 1. Manage channel groups (web client)
Live TV gets a **Groups** button next to the view menu.
- **Create, rename (✎) and delete (🗑) groups.** Deleting a group never deletes channels.
- **Pick channels:** a searchable list of all channels with checkboxes. Newly added channels are appended to the end of the group.
- **Order:** groups and channels can be reordered via drag & drop.
- **"Channels" tab:** tiles with logo, channel number and the program currently airing. Click a tile to play the channel.
- **Per user:** every user has their own groups. Channels a user may not access (parental control, channel restrictions) are never shown.
- **Re-imported M3U:** if channel ids change, channels are matched again by name and number.

### 2. Program guide in the web client ("Program" tab)
Every group has a **timeline program guide (EPG)** of its channels.
- Channels on the left in group order, programs as blocks on a timeline with 30-minute slots.
- A **red line** marks the current time; programs on air are highlighted.
- Color stripes for movies, sports, news and kids; ● marks scheduled recordings.
- **Navigation:** earlier / now / later (3-hour steps) and a day picker for up to 7 days. The window shows 6 hours (3 hours on phones).
- **Click a program** to open the Jellyfin details page (description, play, record). **Click a channel** to play it.
- Program data refreshes every 5 minutes.

### 3. Program guide in TV apps (Fire TV, Android TV, Wholphin, …)
Choose a **program guide group**. Live TV and the built-in program guide of these apps then only show the channels of that group, in group order – playback, recording and details keep working as usual.
- **In the web client:** click **📺** on a group in the groups overview. The active group is shown at the top; **"Alle Sender anzeigen"** removes the filter.
- **On the TV:** Channels → **Live-TV Gruppen** → **📺 Programmführer wählen**, then open a group or "Alle Sender". Afterwards open Live TV → Guide.
- The selection is per user and applies to all of that user's TV apps. The web client is excluded by default and always shows all channels.
- Technically, the plugin filters the response of `GET /LiveTv/Channels` for these apps. If anything goes wrong, the original response is returned unchanged.

### 4. Groups as a channel for apps
Under **Channels → "Live-TV Gruppen"** every group appears as a folder with its channels and logos, playable in the Jellyfin apps for Android TV, Fire TV and mobile.
- Before playback the stream is probed for 3 seconds and the result is cached per channel for 6 hours, so the server can remux instead of transcoding.
- **Note:** Jellyfin's tuner limit does not apply to this path – mind your provider's connection limit. The stream URL is part of the playback info sent to the app.

### 5. Playlists for apps without channel support (optional)
For apps such as **Wholphin**, every group is mirrored as a playlist **"Live-TV: &lt;group&gt;"** of the respective user.
- Playlists update about 2 seconds after each change, at startup and every 6 hours. Run the scheduled task "Live-TV Gruppen als Wiedergabelisten synchronisieren" to update them manually.
- Turning the option off removes these playlists again.

### 6. Settings (Dashboard → Plugins → Live-TV Groups)
| Setting | Description |
| --- | --- |
| Status | Shows whether File Transformation is installed and the web integration is active. |
| Gruppen im Web-Client anzeigen | Groups button and views in the web client. Takes effect after a server restart. |
| Gruppen als Kanal für Apps anbieten | The "Live-TV Gruppen" channel for apps. |
| Gruppen zusätzlich als Wiedergabelisten anlegen | Playlists for apps without channel support. Requires the app channel. |
| Gruppe im TV-Programmführer anzeigen | Enables the program guide filter for TV apps. |
| Apps ohne Gruppenfilter | Comma separated client names that always receive all channels (default: `Jellyfin Web`). |

### Storage
Groups are stored per user as JSON files in `<jellyfin data>/plugins/LiveTvGroups/users/` and survive plugin updates.

## Development

```bash
dotnet test
```

### Release process

Development happens on the `dev` branch; `main` only contains released code.

1. **Development build:** add the changes under `## Unreleased` in `CHANGELOG.md`, then tag the `dev` branch with a four-part version between the last and the next release, e.g. `v0.2.2.1-dev`, `v0.2.2.2-dev`:
   ```bash
   git tag v0.2.2.1-dev && git push origin v0.2.2.1-dev
   ```
   GitHub Actions publishes a **pre-release** and adds the build to `manifest-dev.json`. The public `manifest.json` is not changed.
2. **Test** the build in Jellyfin with the development repository (see below).
3. **Release:** rename `## Unreleased` to `## X.Y.Z`, merge `dev` into `main` and tag `main`:
   ```bash
   git tag v0.2.3 && git push origin v0.2.3
   ```
   GitHub Actions creates the release, adds it to `manifest.json` and removes older development builds from `manifest-dev.json`.

### Testing development builds

Add a second repository in Jellyfin (Dashboard → Plugins → Repositories):
```
https://github.com/iEnki/jellyfin-plugin-livetv-groups/releases/download/dev-channel/manifest-dev.json
```
It lists all stable versions plus development builds marked `[DEV]`. Install the development build from the catalog and restart Jellyfin. The following stable release has a higher version and is offered as a regular update.

License: GPL-3.0

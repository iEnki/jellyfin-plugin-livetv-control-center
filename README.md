# Live-TV Groups for Jellyfin

Personal Live TV channel groups with an independent **Live-TV Gruppen** page in Jellyfin Web.

- **Programme**, **Fernsehprogramm** and **Sender** follow the layout of Jellyfin's Live TV views. The selected group applies to all three views.
- Choose visible groups, a default group and view, remember the last selection and set the guide zoom. **Alle sichtbaren Gruppen** combines their channels without duplicates.
- The guide has channel numbers and logos, 30-minute slots, a current-time indicator, date/time selection, Now, Tonight and seven calendar days. Data refreshes every five minutes; returning from program details preserves the group, time window and scroll position.
- Create, rename and delete groups; search/select channels and reorder groups or channels with drag-and-drop or up/down buttons.
- **Original Live TV stays unchanged in every client.** This plugin no longer filters native Live TV or guide requests. Personal group preferences only affect the separate groups page.
- Apps with channel support can browse **Channels → Live-TV Gruppen**. Optional playlists named **Live-TV: <group>** provide another entry for apps without channel support. These entries do not add a grouped guide to native TV apps.
- Groups and preferences belong to the current user. Only channels permitted by Jellyfin are included.

Built for **Jellyfin 12.0**. Jellyfin 12.1 and native device playback require separate validation; this build does not claim those checks.

## Installation

1. Add this repository in Dashboard → Plugins → Repositories:
   ```
   https://github.com/iEnki/jellyfin-plugin-livetv-groups/releases/latest/download/manifest.json
   ```
2. Add the File Transformation repository:
   ```
   https://www.iamparadox.dev/jellyfin/plugins/manifest.json
   ```
3. Install **Live-TV Groups** and **File Transformation** for your Jellyfin version, then restart Jellyfin.
4. Open **Live-TV Gruppen** through the channels/library entry. If **Plugin Pages 3.x** is installed, the plugin also registers a user-menu entry automatically. Plugin Pages is optional and uses the same group page.
5. Use **Einstellungen** on that page for personal preferences. Global integrations and registration status are under Dashboard → Plugins → Live-TV Groups.

## Upgrading from 0.2.x

Group IDs, channel references and playlist IDs are preserved. Old `ActiveGuideGroupId`, `EnableGuideFilter` and client-exception values no longer activate any filter. The native-guide selection folders and the button/overlay in original Live TV have been removed. After updating and restarting Jellyfin, original Live TV shows all channels permitted by Jellyfin, independently of every group action.

The replacement guide is a separate component built from the original layout reference. It does not overwrite Jellyfin's guide component or global API client. Program clicks open Jellyfin's normal details page, including its playback/recording controls. Channel playback uses the current Jellyfin session, with native details as a fallback.

## Settings

| Location | Setting | Effect |
| --- | --- | --- |
| Groups page | Visible groups | Hides groups from the selector and combined scope without deleting them. |
| Groups page | Default group / view | Initial selection when remembering the last view is disabled. |
| Groups page | Remember last group and view | Saves the selection per user. |
| Groups page | EPG zoom | Compact, normal or large time slots. |
| Dashboard | Web integration | Independent groups page; requires File Transformation and a server restart after changing. |
| Dashboard | App channel | Offers groups through Jellyfin's channel interface. The channel also remains available as the web entry while web integration is enabled. |
| Dashboard | Playlist synchronization | Mirrors groups as user playlists; requires the app channel. Turning it off removes the mirrored playlists. |
| Dashboard | Status | File Transformation registration and optional Plugin Pages installation/registration. Server registration alone does not prove successful client startup. |

## Notes

- Existing Jellyfin EPG data is used. Empty rows mean no programs are available for the selected period; the plugin does not obtain XMLTV data itself. Unresolved channels and discarded invalid program data are reported in the guide.
- After an M3U re-import, channel references are resolved again by ID, then name/number. Ambiguous matches and advanced repair/import/export tools remain future work.
- **App channel and playlists:** Jellyfin cannot open these channel items using its usual Live TV tuner path. The plugin probes the stream and the server remuxes it. Jellyfin's tuner limit does not apply to that path; the stream URL is included in the playback information sent to clients.
- Playlists synchronize shortly after changes, at startup and every six hours. A scheduled task is available for manual synchronization.
- Web integration depends on Jellyfin Web's channel-list route/container. Major client updates may require a plugin update. The current UI is tested on desktop and narrow mobile viewports.
- Groups are stored as per-user JSON in `<jellyfin data>/plugins/LiveTvGroups/users/` and survive updates. Backup that directory before installing development builds.

## Development

```bash
dotnet test --configuration Release
npm ci
npx playwright install chromium
npm run test:web
```

Browser tests use a local Jellyfin-shell/API fixture. They cover navigation isolation, details/back state, preferences, overlapping groups, search, delayed responses/retry, mobile guide labels/date navigation and group CRUD/order. They do not replace testing a newly installed server plugin or native TV apps. To use installed Chrome locally, set `LTVG_BROWSER_CHANNEL=chrome`.

### Release process

Development happens on `dev`; `main` contains stable releases.

1. Add changes under `## Unreleased` in `CHANGELOG.md` and push `dev`.
2. Tag its tested commit with a four-part development version, for example `v0.3.0.1-dev`. Pushing that tag creates a GitHub **pre-release** and updates **manifest-dev.json**. It does not change `main` or the stable manifest.
3. After testing, merge the approved changes into `main`, update the changelog and push a stable tag with a higher version. Stable publication is a separate action.

### Testing development builds

Add a second plugin repository:
```
https://github.com/iEnki/jellyfin-plugin-livetv-groups/releases/download/dev-channel/manifest-dev.json
```
Install the `[DEV]` version and restart Jellyfin. The next stable version must be higher than the development version to be offered as an update.

License: GPL-3.0

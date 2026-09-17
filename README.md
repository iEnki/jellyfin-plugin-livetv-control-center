# Live-TV Groups for Jellyfin

Personal Live TV channel groups with an independent **Live-TV Gruppen** page in Jellyfin Web.

- **Programme**, **Fernsehprogramm** and **Sender** follow the layout of Jellyfin's Live TV views. The selected group applies to all three views.
- Choose visible groups, a default group and view, remember the last selection and set the guide zoom. **Alle sichtbaren Gruppen** combines their channels without duplicates.
- The guide has channel numbers and logos, 30-minute slots, a current-time indicator, date/time selection, Now, Tonight and seven calendar days. Data refreshes every five minutes; returning from program details preserves the group, time window and scroll position.
- Create, rename and delete groups; search/select channels and reorder groups or channels with drag-and-drop or up/down buttons.
- **Original Live TV stays unchanged in every client.** This plugin no longer filters native Live TV or guide requests. Personal group preferences only affect the separate groups page.
- Apps with channel support can browse **Channels → Live-TV Gruppen**. Optional playlists named **Live-TV: <group>** provide another entry for apps without channel support. Each native group folder also offers **Fernsehprogramm → day → channel → program**. Program folders contain timing/episode/description and a clearly labeled **live ansehen** entry. This is a browsable program guide; the stock Fire TV app does not render the web timeline.
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

## Fire TV / Android TV

Install **0.3.0.2** or later from the development catalog, restart Jellyfin, then fully close and reopen the TV app to discard old channel listings. Open **Live-TV Gruppen → group** for sender playback; open its **Fernsehprogramm → day → sender → program** for program information and the sender's **live ansehen** action. This always starts the current live stream, including when the selected program is in the future; it is not catch-up or recording playback.

The Fire TV app uses the native Android TV client. Its standard timeline belongs to original Live TV and does not have a plugin-page/group-route hook. A second timeline inside the stock TV app requires a client change. No filtering or alteration of original Live TV is used as a workaround. App-guide timing can be changed under Dashboard → Plugins → Live-TV Groups.

The server contracts and app navigation hierarchy have automated coverage. End-to-end playback on an actual Fire TV device still needs validation after installation. If it fails, record the selected sender, time and displayed error, and check the corresponding Jellyfin server log. Whether normal Live TV plays that same sender is a useful comparison.

## Remote Live TV (development branch)

On **dev-remote-live-tv**, the independent web/mobile **Live-TV Gruppen** EPG includes **Abspielen auf** and **Geräte aktualisieren**. Choose an active TV, then tap the **channel number/logo** in the guide or its play card in Sender/Programme. Program cells still open native program details. Selecting **Dieses Gerät** restores the existing local behavior.

The chosen target is saved automatically per Jellyfin user as PreferredTargetDeviceId/PreferredTargetDeviceName. A remembered offline target stays selected, with a warning; failed remote commands never start playback on the phone. The server resolves the current session each time. Normally both clients use the same Jellyfin user; controlling another user's TV requires **EnableRemoteControlOfOtherUsers**, and both users must be allowed to play the selected Live TV channel.

V1 requires the official **Jellyfin Android TV** app (also used on Fire TV) open in the foreground with its **internal player**. The server cannot reliably detect foreground state or the external-player setting; a sent command is not playback confirmation. No TV wake-up, app launch, pause/stop panel or catch-up playback is included. Standard remote-capable sessions are also listed, but V1 hardware validation targets Android TV/Fire TV.

### Test this branch

1. Use Jellyfin **12.0** (the plugin targets ABI 12.0.0.0) and the existing web integration dependencies. Back up the per-user plugin data.
2. Download **live-tv-groups-dev** from this branch's successful **Build** action; its artifact ZIP contains the plugin DLL and installation meta.json. Alternatively, check out this branch, run `dotnet publish src/Jellyfin.Plugin.LiveTvGroups --configuration Release --output out`, then `node .github/scripts/write-dev-meta.cjs out`.
3. Stop Jellyfin. Move the previous plugin binaries outside the plugins directory (preserve user data/configuration). Create a directory named **Live-TV Groups_0.3.1.1** under plugins and extract both the DLL and meta.json there. Keep only one installed copy and restart Jellyfin. Confirm plugin version **0.3.1.1**.
4. Reload/reopen Jellyfin Web or the web-based mobile client. On the Fire TV, open Jellyfin using the same user and disable **Use external player**.
5. Open **Live-TV Gruppen → Fernsehprogramm**, refresh devices, choose the TV, and tap a channel's number/logo. Verify the channel starts on the TV while the phone keeps the EPG; tap another channel to switch.
6. Reopen the EPG to check the target is remembered. Restart the TV app, refresh devices, and play again to exercise new-session resolution. Close the TV app and check unavailable-target/error behavior; select **Dieses Gerät** to return to local playback.

Version ordering is **0.3.1.0 < 0.3.1.1 < 0.3.2.0**. The stable manifest remains unchanged. The packaged meta.json records version 0.3.1.1, the existing plugin GUID/ABI, and autoUpdate=true so the manual installation retains automatic future updates. Manual/branch-artifact installation and dev-catalog installation are both supported. For direct Jellyfin installation, add https://github.com/iEnki/jellyfin-plugin-livetv-groups/releases/download/dev-channel/manifest-dev.json and select version 0.3.1.1. The dev-release tag workflow accepts commits reachable from origin/dev or origin/dev-remote-live-tv and requires the tag version to match the project version. The v0.3.1.1-dev pre-release publishes this branch to the shared dev catalog. Before the stable release, set Version to **0.3.2.0** and remove the development InformationalVersion override. Jellyfin can then offer the higher stable version through the existing stable catalog.

## Settings

| Location | Setting | Effect |
| --- | --- | --- |
| Groups page | Visible groups | Hides groups from the selector and combined scope without deleting them. |
| Groups page | Default group / view | Initial selection when remembering the last view is disabled. |
| Groups page | Remember last group and view | Saves the selection per user. |
| Groups page | EPG zoom | Compact, normal or large time slots. |
| Dashboard | Web integration | Independent groups page; requires File Transformation and a server restart after changing. |
| Dashboard | App channel | Offers groups through Jellyfin's channel interface. The channel also remains available as the web entry while web integration is enabled. |
| Dashboard | App-guide timezone | Timezone used in native program lists; defaults to Europe/Vienna. The web guide continues to use the device timezone. |
| Dashboard | Playlist synchronization | Mirrors groups as user playlists; requires the app channel. Turning it off removes the mirrored playlists. |
| Dashboard | Status | File Transformation registration and optional Plugin Pages installation/registration. Server registration alone does not prove successful client startup. |

## Notes

- Existing Jellyfin EPG data is used. Empty rows mean no programs are available for the selected period; the plugin does not obtain XMLTV data itself. Unresolved channels and discarded invalid program data are reported in the guide.
- After an M3U re-import, channel references are resolved again by ID, then name/number. Ambiguous matches and advanced repair/import/export tools remain future work.
- **App channel and playlists:** a dedicated media-source provider delegates source discovery and stream opening to Jellyfin's native Live TV provider. The real live-stream object is retained for sharing, probing, closing and native tuner handling. The plugin no longer opens/probes provider URLs independently or bypasses the normal tuner path. Native media-source delivery behavior still applies; this is not a new URL-hiding proxy.
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

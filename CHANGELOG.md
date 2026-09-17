# Changelog

## Unreleased

- New independent **Live-TV Gruppen** page with Programme, Fernsehprogramm and Sender views based on Jellyfin's original Live TV layout.
- Group selection, deduplicated visible-group scope and personal settings for visible/default groups, initial/last view and guide zoom.
- Improved guide navigation, current-time mode, automatic refresh, mobile program labels and restoration of group/time/scroll state after details.
- Searchable sender picker with selected-only filtering, keyboard-friendly sorting, cancellable dialogs, request cancellation and retryable errors.
- Optional Plugin Pages 3.x user-menu entry with channel-entry fallback and dashboard registration status.
- Removed native Live TV/EPG filtering, client exception settings, native-guide selection folders and the groups overlay/button in original Live TV. Original Live TV requests remain untouched in every client, including upgrades with old filter values.
- Preserve existing groups, channel references and playlist IDs; preferences are stored separately per user.
- Added browser regression checks to CI and backend checks for migration, user isolation and valid group-guide program data.
- Development build targets Jellyfin 12.0. Native TV-device playback and Jellyfin 12.1 are not validated by this release.

## 0.2.2

- Fix: the "Groups" button was missing in Live TV since 0.2.1. The script is registered for index.html again (shared with other plugins such as Jellyfin Enhanced) and is only added to real HTML documents, so JavaScript files stay untouched.

## 0.2.1

- Fix: the Jellyfin web client could no longer be opened (blank login page, "Unexpected token '<'"). The script tag was also injected into JavaScript files of the web client; it is now only added to index.html.
- Recommended update for everyone using 0.1.0 – 0.2.0.

## 0.2.0

- New: "Program" tab for every group in the web client – timeline program guide (EPG) of the group's channels with day selection, now indicator and categories. Click a program for details, click a channel to play it.
- New: program guide filter for TV apps (Fire TV / Android TV, Wholphin, …). Pick a group via 📺 in the web client or on the TV under Channels → Live-TV Gruppen → "Programmführer wählen"; Live TV and the guide of these apps then show only that group.
- New settings: enable/disable the guide filter and apps that always get all channels (default: Jellyfin Web).
- Known issue: breaks loading the web client – update to 0.2.1.

## 0.1.1

- Fix: channel logos were missing in the "Live-TV Gruppen" channel. Logos are now stored locally before the channel entries are created.

## 0.1.0

- Personal channel groups per user: create, rename, delete and reorder groups, pick channels and play them from Live TV → Groups in the web client.
- Groups as channel "Live-TV Gruppen" for apps (Android TV, mobile, …), including stream probing for reliable playback.
- Optional playlists "Live-TV: <group>" for apps without channel support (e.g. Wholphin).
- Channels are matched again by name and number after the M3U is re-imported; channels a user may not access stay hidden.

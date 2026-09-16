# Changelog

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

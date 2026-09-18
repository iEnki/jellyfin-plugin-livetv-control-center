# Changelog

## Unreleased

### Beta 0.3.3.1: central channel access

- Add administrator-managed named channel rules independently of personal/shared group mode, disabled by default. New rules allow selected users only; everyone-except-selected is available, and overlapping denials win.
- Apply restrictions to channel selection, existing groups, EPG, native and original Live TV, playlists, direct playback, stream identities, recording controls and attributed recordings, while preserving existing Jellyfin permissions.
- Preserve hidden group references/order and restore them when access is granted; show only authorized content and counts.
- Add English/German administration with source/name/number search, bulk channel selection, user policies, effective preview, active-playback impact, recording assignment and revision conflicts.
- Resolve rescanned channels using source/external identity; show missing/ambiguous mappings for correction and leave unclassified new channels visible.
- Capture source attribution for new built-in DVR recordings without relying on NFO tags. Keep unidentified old recordings unchanged until manually assigned.
- Stop denied sessions/HTTP consumers/device transcoding jobs without closing another authorized viewer's shared tuner. Protect source URLs and legacy HLS ownership.
- Add policy, browser and supported Jellyfin 12 API-controller integration tests. Keep the stable release and installable beta/development repositories separate.

## 0.3.3.0

- Promote the tested beta features to stable 0.3.3.0. Stable and beta installations can update normally while preserving existing settings and groups.

- Add an Edit channels button to existing group cards, with fresh group-specific selection, preserved channel order and unchanged editing permissions.

- Add the Live-TV Groups plugin logo to the catalog, installed-plugin metadata and all installation packages.

- Add an installable beta channel with a dedicated repository URL, automatic catalog publication and compatibility with the existing development repository.

- Add an optional administrator setting to hide the original Live TV My Media entry in web and web-based mobile clients when a usable groups entry is visible.
- Keep the Live TV menu, recordings, permissions, server streaming and native TV apps unchanged; restore the original entry when groups are unavailable or the user changes.
- Localize the dashboard option in English and German and document its web-only behavior.

## 0.3.2.1

### Language and display name

- Add English and German interface translations following Jellyfin display language; other interface languages fall back to English.
- Use client locale for web dates and times. Native guide labels use the language sent by the client, falling back to server display language.
- Add an optional administrator-defined display name; empty restores the translated default. The shared library name follows server language, while the web title follows client language.
- Update the optional Plugin Pages shortcut after restart. Preserve channel identity, user permissions, groups, stream ownership and playlist mappings when changing display names.
- Separate native guide caches and program metadata by language/culture. User-created names and imported EPG text remain unchanged.
- Provide fully English user documentation, including language selection and custom display-name instructions.

### Installation

- Publish stable 0.3.2.1 with matching DLL and installation metadata. Existing stable 0.3.2.0 installations can update normally.
- Development build 0.3.2.1 uses the same numeric version. Jellyfin does not treat this stable package as a higher version; reinstall the stable package manually to replace that development binary. Preserve plugin data and configuration.

## 0.3.2.0

### Remote Live TV playback

- Select an active Jellyfin Android TV / Fire TV target from the independent web/mobile program guide and start a channel on the TV while keeping the guide open.
- Discover active official Android TV sessions even when SupportsRemoteControl is false, without bypassing user permissions.
- Remember the preferred device per user and resolve its current session for every command.
- Switch running Android TV channels with an authorized Stop, a stopped-report wait and player cleanup delay, followed by one native PlayNow. Serialize commands per device and handle timeout, cancellation, session changes and permission revocation.
- Keep offline preferred targets selected and show remote failures without silently falling back to playback on the phone.

### Personal and central groups

- Choose personal groups per user or a central collection administered by administrators.
- Optionally copy administrator personal groups into the central collection without deleting originals or overwriting already imported IDs. Switching modes preserves both collections.
- Configure each central group for all users except selected exclusions, or only selected users. Administrator management access is retained.
- Apply group permissions to the independent guide, native group/EPG folders, plugin stream opening and both users involved in remote playback. Original Jellyfin Live TV remains unchanged.
- Make central collections read-only for ordinary users while keeping display and player preferences personal.

### Documentation and delivery

- Add a direct Fernsehprogramm button and document the Jellyfin channel permission needed for the My Media entry.
- Provide a complete English README covering installation, permissions, group administration, all views, remote playback, native apps, playlists, settings, storage, troubleshooting and development.
- Publish stable version 0.3.2.0 with matching installation metadata and automatic update support. Both development builds 0.3.1.1 and 0.3.1.2 are lower than this release.
- Require stable tags to match the project version and reference main; run plugin, browser and package tests before publication.
- The maintainer confirmed the 0.3.1.2 development build works as intended on their setup. External players, automatic TV wake-up and untested Jellyfin/client versions remain outside the supported scope.

## 0.3.1

Web client
- New independent **Live-TV Gruppen** page with Programme, Fernsehprogramm and Sender views based on Jellyfin's original Live TV layout.
- Group selection, deduplicated "all visible groups" scope and personal settings for visible/default groups, initial/last view and guide zoom.
- Improved guide: navigation, current-time mode, automatic refresh, mobile program labels and restored group/time/scroll state after opening details.
- Searchable channel picker with selected-only filter, keyboard-friendly sorting, cancellable dialogs, request cancellation and retryable errors.
- Optional Plugin Pages 3.x user-menu entry with channel-entry fallback and registration status in the dashboard.

Apps (Fire TV / Android TV)
- Each app group now has a **Fernsehprogramm** folder: seven calendar days, channel lists, program times/episodes/descriptions and an explicit "live ansehen" action.
- Playback of group channels uses a dedicated media-source provider with Jellyfin's native Live TV open/close lifecycle instead of raw tuner URLs and own stream probing.
- Group membership and channel permissions are checked again when sources are discovered and streams are opened.
- Configurable app-guide timezone, correct 23/25-hour days at DST changes, five-minute channel cache refresh.

Changes and compatibility
- Removed the Live TV/guide filter for native apps, its client exclusion setting, the guide selection folders and the groups button/overlay in the original Live TV. Original Live TV is no longer modified in any client, including upgrades with old filter values.
- Existing groups, channel references and playlist IDs are preserved; personal preferences are stored separately per user.
- Built for Jellyfin 12.0. Playback on actual TV hardware and Jellyfin 12.1 are not validated by automated tests.

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

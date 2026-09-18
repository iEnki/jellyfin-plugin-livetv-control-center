# Changelog

## Unreleased

### 0.3.3.3-beta: real native guide activation and official TV playback compatibility

- Add an explicit Native TV guide action inside each group and All channels reset at the channel root, usable with the TV remote. Set the existing scope for the authenticated TV, then navigate via DisplayContent to Jellyfin's real Live TV UserView; select TV Guide there to open the original timeline.
- Handle native Items, legacy user Items and Channels item requests through a per-request MVC filter with canonical action folders. Provider/background/cache calls never select groups or send navigation. Validate own-user token/session/device, plugin channel visibility and current group/channel rights; debounce duplicate group visits.
- Fix a 0.3.3.2 filter gap: MVC omits absent nullable pagination arguments. Filter requests without startIndex/limit as well as paged requests, restoring original arguments after the native action.
- Fix exact official client-name detection shared by remote playback and native filtering: recognize Android TV, Jellyfin for Android TV and its current debug name alongside Jellyfin Android TV. Connected official TV sessions without SupportsRemoteControl no longer disappear merely because of their client name. Keep live transport, user/channel authorization and authenticated controlling-session requirements.
- Clearly label the existing folder guide as Program list (fallback), preserving its IDs, browsing and playback; invalidate stale provider layouts. Web EPG and LiveTv/Programs remain unchanged.
- Navigation failures, missing connection/command support or active playback keep the selected group and provide manual Live TV → TV Guide instructions. A direct timeline jump is not supported; cached guides may require reopening. Physical Fire TV acceptance remains outstanding.
- Add service/filter/native-controller and browser regression tests. Target Jellyfin 12 / net10.0; publish only through the separate beta catalogs/prerelease workflow.

## 0.3.3.2-beta

### 0.3.3.2-beta: native Android TV / Fire TV guide

- Add an opt-in, plugin-only MVC filter for the original Jellyfin Live TV channel list and native TV guide. Preserve real channel IDs, native DTOs, sort/query options and playback; no virtual channels, ILiveTvService or custom TV client.
- Persist explicit group selection per authenticated user/device, limited to the official Android TV client. Ignore old guide settings and keep other clients/devices/users unchanged.
- Filter before native pagination and correct totals. Revalidate group/channel access and repaired source IDs on every request; deleted/denied/empty groups or plugin failures restore ordinary authorized Live TV. Keep existing central channel restrictions authoritative.
- Add current-device and own-user target GET/PUT/DELETE NativeGuide endpoints, an offline-safe All channels reset, and English/German Use group on TV / All channels on TV actions using the existing device selector.
- Keep /LiveTv/Programs, independent web EPG, browsable native fallback and remote playback unchanged. Open ordinary Live TV → TV Guide manually; reopening the view/app may be required due to caching. Cross-user scope transfer and DisplayContent/direct timeline navigation are not included.
- Add scope/filter/controller tests, real Jellyfin-12 native channel/program integration coverage, browser regressions and a documented phased development plan. Publish only through the existing beta release/catalog workflow.

## 0.3.3.1

### Central channel access

- Add administrator-managed named channel rules independently of personal/shared group mode, disabled by default. New rules allow selected users only; everyone-except-selected is available, and overlapping denials win.
- Apply restrictions to channel selection, existing groups, EPG, native and original Live TV, playlists, direct playback, stream identities, recording controls and attributed recordings, while preserving existing Jellyfin permissions.
- Preserve hidden group references/order and restore them when access is granted; show only authorized content and counts.
- Add English/German administration with source/name/number search, bulk channel selection, user policies, effective preview, active-playback impact, recording assignment and revision conflicts.
- Resolve rescanned channels using source/external identity; show missing/ambiguous mappings for correction and leave unclassified new channels visible.
- Capture source attribution for new built-in DVR recordings without relying on NFO tags. Keep unidentified old recordings unchanged until manually assigned.
- Stop denied sessions/HTTP consumers/device transcoding jobs without closing another authorized viewer's shared tuner. Protect source URLs and legacy HLS ownership.
- Add policy, browser and supported Jellyfin 12 API-controller integration tests.

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

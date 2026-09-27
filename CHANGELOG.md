# Changelog

## Unreleased

- Administrators can now copy central groups into their own personal collection when switching back to personal mode, or import them later. Both management screens require an explicit copy/skip choice on mode switch. Existing personal groups take precedence when IDs match; central groups and other users' personal groups remain untouched. Custom group images are copied with newly imported groups.
- Require administrators to explicitly choose whether to copy their personal groups when switching to central management. The no-copy choice preserves personal groups for later use, and both the web dialog and plugin dashboard offer a later import. Reject an ambiguous mode-switch request instead of silently showing an empty central group list.

## 1.2.0.0

- Support Wholphin in the native Live TV guide: select a group or **All channels** directly in its compatible Control Center folders, then open **Live TV → TV Guide**. Group selection applies only to the authenticated user's device, respects current group/channel permissions, and restores the full guide on reset. Wholphin does not support an automatic guide jump.
- Add administrator switches for the official Jellyfin Android TV / Fire TV app and Wholphin. A sole eligible TV is selected automatically; with several TVs, the last available choice is remembered per user. Disabled or disconnected devices cannot receive playback or guide changes.
- Add recognizable artwork to the Control Center home tile, guide actions, confirmation and fallback folders. **All channels** has its own EPG-reset image; created groups have a distinct group image. Personal and shared groups can use custom PNG, JPEG or WebP images up to 5 MiB, with preview and reset controls and permission checks.
- Add recording controls to the grouped web EPG. Users with Jellyfin's Live TV management permission can schedule, edit and cancel single-program or series timers, including padding and series options; recording errors remain visible in the dialog.
- Restore reliable loading of the independent web EPG behind reverse proxies that cached its old script URL. Preserve original channel logos, official Android TV / Fire TV behavior, channel playback and the existing plugin identity and settings.

## 1.1.0.0

- Selecting a group in the official Android TV / Fire TV app immediately applies that group's native guide scope and opens the ordinary Live TV view. The separate Native TV guide action remains available to retry.
- Hide direct grouped-channel media tiles from that authenticated TV response while keeping the native guide action and browsable program-list fallback. Internal channel items and playlists remain unchanged.
- Retain per-user/device validation, group access checks and safe behavior when navigation is unavailable. The original app may still require selecting TV Guide after opening Live TV; a direct timeline deep link is not exposed by the stock client.

## 1.0.0.0

- Organize Live TV channels in ordered personal groups independent of IPTV-provider categories, or use administrator-managed shared groups with per-group user access.
- Show one selected group's original channels in Jellyfin's native Android TV / Fire TV timeline. Select the group on the TV or from the web/mobile page; the web page can also apply the deduplicated union of all visible groups. Reset to all ordinarily permitted channels at any time. Selections are per user and TV device.
- Browse an independent web EPG with a channel/program timeline, now/next/later programs, channel cards, day/time navigation and zoom. Save visible/default groups and view preferences; use the interface in English or German.
- Use the web or phone guide as a remote for the official Jellyfin Android TV / Fire TV app: discover a compatible TV, remember the target, and start or switch live channels while the guide stays open.
- Apply optional central channel-access rules to selected users, including children's accounts. Restrictions cover the plugin and ordinary Jellyfin Live TV, direct playback, playlists and associated recordings while preserving Jellyfin permissions.
- Browse grouped channel and daily program folders in supported native apps; optionally mirror groups as playlists for other clients.
- Integrate the web interface through File Transformation, optionally add a Plugin Pages menu shortcut, customize the display name and hide the original Live TV home tile when the groups entry is available.
- Support Jellyfin 12.0 or later in the Jellyfin 12 series (12.1 confirmed). Keep the existing plugin ID, channel identity and stored settings for upgrades from Live-TV Groups. Native guide navigation may require one additional TV Guide selection; no virtual channels or custom TV client are used.

# Changelog

## Unreleased

- Beta 1.1.0.8: Restore the independent web EPG when a reverse proxy returns a cached 504 for the old versioned client script URL. Use a new version parameter for the injected script; native TV folders and guide filtering are unchanged.

- Beta 1.1.0.7: Give **All channels** a dedicated EPG-reset image and created groups a separate grouped-channel folder image. Existing custom group images remain unchanged, and the channel data version refreshes cached TV tiles.

- Beta 1.1.0.6: Add built-in Primary artwork for the Live-TV Control Center, native-guide actions, confirmation folders and the fallback program list so Jellyfin TV and Wholphin show recognizable tiles.
- Add per-group PNG, JPEG or WebP uploads up to 5 MiB with preview/reset controls, Jellyfin decoder validation, personal/shared permissions, import copying, persistent storage and cache-tag refreshes. Square center-weighted images are recommended for the clients' different card crops.
- Preserve original channel logos, Wholphin folder conversion, guide filtering and playback behavior.

- Beta 1.1.0.5: Treat Wholphin's authenticated group-folder request as the active device signal even when the client exposes no remote-command controller. Group and All channels selection now update the device guide scope and replace unplayable channel tiles with the guide confirmation.

- Beta 1.1.0.4: Keep Live-TV Control Center visible in Wholphin's navigation by advertising the root as a supported Folders collection, while child group entries retain the neutral collection type required to avoid premature server filtering.

- Beta 1.1.0.3: Make Live-TV Control Center and its group entries browsable in Wholphin by presenting them as standard folders only to that client. Selecting a group or All channels now updates the authenticated Wholphin device scope from the TV; the user then opens Live TV → TV Guide manually.
- Require the matching active Wholphin session, retain current group and channel permission validation, hide the unsupported channel entry when Wholphin targets are disabled, and never send Wholphin a DisplayContent command.

- Beta 1.1.0.2: Add administrator switches for official Jellyfin Android TV / Fire TV and Wholphin target apps. Auto-select a sole eligible TV; with several TVs, preserve the user's last available choice and require a choice when it is unavailable. Block hidden or disconnected targets from web guide playback and scope changes.
- Beta: Allow an authenticated Wholphin device to use a per-user/device native guide scope selected from the plugin web/mobile page. Wholphin's own Live TV guide then receives only the selected group's channels (or all visible groups); reset restores ordinary channels. Wholphin guide navigation remains manual.
- Keep official Android TV / Fire TV navigation and remote playback behavior unchanged. Add Wholphin-specific guidance and regression coverage.

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

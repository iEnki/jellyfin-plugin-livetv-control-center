# Changelog

## Unreleased

## 1.1.0.0

- Selecting a group in the official Android TV / Fire TV app immediately applies that group's native guide scope and opens the ordinary Live TV view. The separate Native TV guide action remains available to retry.
- Hide direct grouped-channel media tiles from that authenticated TV response while keeping the native guide action and browsable program-list fallback. Internal channel items and playlists remain unchanged.
- Retain per-user/device validation, group access checks and safe behavior when navigation is unavailable. The original app may still require selecting TV Guide after opening Live TV; a direct timeline deep link is not exposed by the stock client.
- Promote the owner-tested 1.1.0.0 beta without changing its plugin identity or saved settings.

## 1.0.0.0

- Organize Live TV channels in ordered personal groups independent of IPTV-provider categories, or use administrator-managed shared groups with per-group user access.
- Show one selected group's original channels in Jellyfin's native Android TV / Fire TV timeline. Select the group on the TV or from the web/mobile page; the web page can also apply the deduplicated union of all visible groups. Reset to all ordinarily permitted channels at any time. Selections are per user and TV device.
- Browse an independent web EPG with a channel/program timeline, now/next/later programs, channel cards, day/time navigation and zoom. Save visible/default groups and view preferences; use the interface in English or German.
- Use the web or phone guide as a remote for the official Jellyfin Android TV / Fire TV app: discover a compatible TV, remember the target, and start or switch live channels while the guide stays open.
- Apply optional central channel-access rules to selected users, including children's accounts. Restrictions cover the plugin and ordinary Jellyfin Live TV, direct playback, playlists and associated recordings while preserving Jellyfin permissions.
- Browse grouped channel and daily program folders in supported native apps; optionally mirror groups as playlists for other clients.
- Integrate the web interface through File Transformation, optionally add a Plugin Pages menu shortcut, customize the display name and hide the original Live TV home tile when the groups entry is available.
- Support Jellyfin 12.0 or later in the Jellyfin 12 series (12.1 confirmed). Keep the existing plugin ID, channel identity and stored settings for upgrades from Live-TV Groups. Native guide navigation may require one additional TV Guide selection; no virtual channels or custom TV client are used.

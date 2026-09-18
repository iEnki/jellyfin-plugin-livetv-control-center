# Deferred plan: central channel access

Status: deferred at the user's explicit request. Do not implement as part of stable 0.3.3.0. Revisit this plan with the user before implementing.

## Agreed behavior

Administrators manage named rules such as Adult, independently of personal/shared group mode. Restrictions apply in Live-TV Groups, ordinary Jellyfin Live TV and native TV apps, including associated recordings and recording functions. New rules default to allowing only explicitly selected ordinary users; newly created users are excluded. Offer Everyone except selected users as the alternative. Administrators retain management access, subject to existing Jellyfin permissions. Any applicable denial takes precedence; rules never grant missing Jellyfin permissions.

New channels without a rule are immediately visible, per the user's choice. Do not automatically classify Adult channels from names or EPG ratings. Newly denied active playback must stop, without interrupting other authorized viewers of a shared tuner. Already delivered client buffers can remain briefly visible.

## Administration

Add English/German Channel access administration in the dashboard and plugin administration dialog, restricted to administrators. Provide rule CRUD, searchable channel selection by name/number/source, multi-select and select filtered results, user access selection, and effective per-user preview with denial reasons. Show affected users/active playback before saving. Present missing/ambiguous channel mappings and recordings without source attribution for administrator correction.

## Server architecture

Add a common ChannelAccessService and use it in channel selection, group editing, EPG, native group folders, playlists and playback. Remote playback checks both controlling and target-session users, including checks during channel changes. Preserve hidden references in existing groups and while editing; display only authorized channel counts/content and restore hidden entries after access is restored. Invalidate user/culture caches on policy changes.

Bridge native list filtering using uniquely owned Jellyfin tags and user tag restrictions, preserving all unrelated tags and parental permissions. Add a global authenticated API filter through plugin service registration to enforce direct item details, playback, stream/segment, download, recording and remote command paths. Tags alone are not a playback guard: inspected official StreamingHelpers code loads the item without a user visibility filter. Validate against the plugin's supported Jellyfin version before shipping. Resolve item IDs, native/group open tokens and stream IDs to canonical channels; reject contradictory identities and bypasses.

Revoke targeted active HTTP streaming connections and transcoding/playback sessions when access changes; do not close shared tuners for authorized viewers. Keep restrictions persisted across ordinary shutdown/restart. Explicitly disabling this feature removes only plugin-owned tags/restrictions.

Use Jellyfin item ID plus service/source and external channel identity. Do not use channel names as the sole security key. Ambiguous rescan matches remain restricted pending administrator review. Associate recordings through reliable source metadata and persist attribution for new recordings. Unidentified old recordings keep their existing Jellyfin rights until manually attributed; exported files require their own Jellyfin rights.

Persist versioned rules with the central administration document. Existing installations migrate with no new restrictions. Add administrator-only inventory/rule/preview endpoints under /LiveTvGroups/Administration/ChannelAccess. Use revisions and reject stale updates with a conflict. Keep existing group/playback interfaces compatible.

## Acceptance and delivery

Test both group modes, admins/allowed/denied/new users, overlapping rules, new channels, restarts, rescans, duplicate names, recordings, direct IDs, old tokens, native lists with correct pagination/counts, EPG, favorites/playlists, active revocation and simultaneous authorized viewers. Add English/German dashboard/browser tests and integration tests against the supported Jellyfin server. Run existing rights/guide/remote suites and plugin build. Test Fire TV where a device is available; otherwise report hardware validation as outstanding.

Implement on a new branch based on current origin/beta, with main and stable release unchanged until separately authorized. Publish every intended test build as a numerically newer four-part beta directly installable through the persistent beta and legacy dev catalogs. Later stable promotion must be newer than all published previews. Update the English user README and workflow notes; move implementation notes to review after successful verification.

## Sources inspected during planning

- https://jellyfin.org/docs/general/server/users/adding-managing-users/
- https://github.com/jellyfin/jellyfin/blob/master/Jellyfin.Api/Helpers/StreamingHelpers.cs
- Existing GroupService, GroupStore, GroupsController, PlayerService and native groups media-source architecture.

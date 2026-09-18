# Corrective beta 0.3.3.3: select a native guide group with the TV remote

## User feedback and intended behavior

The user supplied two TV photographs: the plugin currently opens a browsable grid of program folders, whereas the desired screen is Jellyfin's original time/channel grid containing only the selected group's channels. Beta 0.3.3.2 implemented scoped filtering but exposed activation only on the web page; its existing native Fernsehprogramm folder still opened the old program list. The corrected native flow must work with the TV remote, without a phone, new service or custom client.

Archon remains unused, as clarified by the user. Track research, doing, implementation notes and review in this document. Version 0.3.3.2-beta is already published; use 0.3.3.3-beta for the fix so Jellyfin offers an update. Never overwrite published package/catalog checksums or publish to stable.

## Verified client/server evidence

- Official Android TV revision 8dabe700778212f3246b316250cc0464b5baba33: SocketHandler.kt supports DisplayContent and resolves UserView through itemLauncher.launchUserView. ItemLauncher.java routes CollectionType.LIVETV to the native Live TV smart screen. The command is ignored during active/paused playback.
- A Guide command is not registered by SocketHandler; no plugin-only direct timeline navigation is promised. One TV Guide selection on ordinary Live TV remains necessary.
- GenericFolderFragment loads ChannelFolderItem children through GetItemsRequest (/Items), not exclusively /Channels/{channelId}/Items. A correction must handle both canonical MVC actions, including legacy Items action, while leaving unrelated item reads untouched.
- Jellyfin 12 source revision 6c073e19ddf604b2369c638716164fdab4c952dc: GetInternalLiveTvFolder returns the genuine named livetv UserView. ChannelManager maps folder ExternalId into provider FolderId and caches provider results. Action activation therefore belongs in a per-request MVC filter, not in IChannel.GetChannelItems, which may be cached or run during background scans.
- TvManager.loadAllChannels fetches the native channel list; scope filtering from 0.3.3.2 already preserves original IDs and filters before pagination. Opening a fresh guide triggers loading; resuming an old guide can keep client cache.

## Design

1. Add a native-guide action folder to each group, prominently named Native TV guide / Natives Fernsehprogramm. Rename the old guide label to Program list (fallback) / Programmliste (Fallback), retaining its original IDs and behavior. Add All channels (native guide) at the plugin root. Bump provider DataVersion to invalidate old cached folder layouts.
2. Provider action folders return a readable instruction item; provider calls never mutate scopes or send commands. Background library refreshes therefore cannot activate a group.
3. Register NativeGuideActionFilter using PostConfigure<MvcOptions>. Match exact native Items/Channels controller identities and explicit parent/folder action IDs in the plugin's canonical channel. Skip recursive searches, filtered latest rows, API keys, missing identity, other clients/users and non-action folders.
4. After successful native action execution, verify the real user's plugin channel access via IChannelManager, resolve the authentication token to the requesting active own-user official TV session and revalidate group/channel rights. Save/clear the existing scope for that authenticated device. Never scope from display names or transfer another user's group.
5. Send DisplayContent with ItemId of the real Live TV UserView, ItemType=UserView, and the actual caller's session ID as controllingSessionId. Do not interrupt playback. Check supported commands and live transport. Revalidate identity before sending. If navigation cannot run, keep the successfully selected scope and show manual Live TV → TV Guide instructions; do not claim delivery proves client navigation.
6. Suppress duplicate activation/navigation from simultaneous/repeated folder loads with a bounded per-device action debounce; do not debounce a reset or a different group. Keep web actions and native fallback usable.

## Tasks

### 1. Native actions and request integration — review

Research above completed before code changes. Implement IDs/items, cache invalidation, per-request action filter and lazy action service. Confirm browsing a group alone, old program lists and background provider calls cannot change scopes.

### 2. Transport, permissions and tests — review

Validate caller token/session/device/client, plugin channel visibility, group/channel access, self-control, supported DisplayContent, active playback, missing view/transport, duplicate loads, reset, deleted groups and spoofed/foreign routes. Add unit tests and actual Jellyfin Items/Channels controller tests. Existing scoped filtering remains unchanged.

### 3. Documentation and beta delivery — review

Document remote-only TV flow and the distinction between native timeline and list fallback. Record one extra guide click and possible cached-view reopen. Run all .NET/native API, web, syntax and package checks, build net10.0, publish via existing beta branch workflow, then verify ZIP/catalog/pre-release/unchanged stable.

## Acceptance

Using the TV remote: Live-TV Groups → created group → Native TV guide → original Live TV → TV Guide opens the native time/channel grid with only permitted channels of the selected group. Reset restores the ordinary authorized list. No phone is required. The plugin's folder grid is clearly labeled as fallback, never advertised as the native timeline. User/device isolation and current permissions remain intact. Physical TV navigation/rendering can only be accepted on a real device; server-side integration and command payloads will be tested here.

## Source links

- https://github.com/jellyfin/jellyfin-androidtv/blob/8dabe700778212f3246b316250cc0464b5baba33/app/src/main/java/org/jellyfin/androidtv/data/eventhandling/SocketHandler.kt
- https://github.com/jellyfin/jellyfin-androidtv/blob/8dabe700778212f3246b316250cc0464b5baba33/app/src/main/java/org/jellyfin/androidtv/ui/itemhandling/ItemLauncher.java
- https://github.com/jellyfin/jellyfin-androidtv/blob/8dabe700778212f3246b316250cc0464b5baba33/app/src/main/java/org/jellyfin/androidtv/ui/browsing/GenericFolderFragment.kt
- https://github.com/jellyfin/jellyfin/blob/6c073e19ddf604b2369c638716164fdab4c952dc/src/Jellyfin.LiveTv/Channels/ChannelManager.cs

### 4. Wohnzimmer Fire TV discovery/playback regression — review

Research completed before edits: PlayerService and PlayersController are unchanged between v0.3.3.0 and v0.3.3.1. The current official Android TV AppModule registers ClientInfo("Jellyfin for Android TV", ...), with an optional exact " (debug)" suffix. Official v0.18.11 registers "Android TV". Existing plugin code only accepts "Jellyfin Android TV", excluding real official sessions whenever SupportsRemoteControl is false. This same mismatch affects the 0.3.3.2 guide filter/controller/web button and the correction's action filter. Fix these through one server helper and an equivalent exact web allowlist; retain user permissions and the requirement for a live controller. Test discovery, actual PlayNow dispatch, guide activation/filtering and web controls for the actual names. The user's specific device session/client/logs are unavailable, so this is a verified compatibility defect, not yet a verified diagnosis of the physical stick.

Sources researched: current pinned AppModule.kt above and https://github.com/jellyfin/jellyfin-androidtv/blob/v0.18.11/app/src/main/java/org/jellyfin/androidtv/di/AppModule.kt

Task 1 implementation notes: explicit group/reset folders and cache DataVersion 9 added. Action filter handles real Items/Channels successful results and uses canonical Folder.ExternalId, real token/session and channel visibility. DisplayContent uses the genuine livetv UserView and authenticated self-control; failed navigation retains the saved scope. Scope selection itself does not require a WebSocket because its authenticated HTTP request is sufficient; automatic navigation does require the live message transport. Playback is never interrupted. Current user rights are rechecked after waiting for the action lock.

Task 1 status: review. Task 2 checked next, research completed above; status: doing.

Task 4 implementation notes: server client detection centralized in PlayerService.IsAndroidTvClient and used by discovery, native controller/channel filter/action filter/action service. Web guide activation accepts the same exact official names. Permissions and active transport requirements for remote playback are preserved. Added discovery/preference/PlayNow dispatch tests for all recognized names and real-client web regression tests. Status: review, pending full suite.

Task 2 integration finding: actual MVC ActionArguments omit absent nullable parameters. The 0.3.3.2 native filter incorrectly required both startIndex and limit dictionary entries, failing open for the native all-channel/first-page request. Research/PoC from the real Jellyfin API test completed before correction. Always supply null for the known canonical action's pagination arguments while obtaining authorized channels; restore original entries or remove previously absent entries in finally. Validate real /LiveTv/Channels with neither parameter, limit only, startIndex only and both parameters after each native action route.


Task 2 implementation notes and review: added 33 action/service/filter cases plus real MVC coverage of Items.GetItems, Items.GetItemsByUserIdLegacy and Channels.GetChannelItems; each selection tests all four pagination-parameter combinations against LiveTv.GetLiveTvChannels, then resets through the same native route. Test duplicate reads, group switching, own-device isolation, wrong channels, old fallback, background provider reads, API keys, foreign identities, channel visibility, deletion/rights, failed core responses, missing transport/DisplayContent/view, active playback and send failures. Full .NET suite including the real pinned API: 190 passed, 0 failed/skipped. Existing source-specific channel restrictions and Programs pass-through tests remain green. Status: review. Task 3 checked next and set doing; documentation and existing prerelease workflow researched above before version/docs edits.

Final local validation: 193 .NET tests passed with JELLYFIN_NATIVE_API set (0 failed, 0 skipped); both actual native integration facts executed. All 40 Chromium web tests passed, including real official client names, remote playback and native-guide activation/reset. One package metadata test passed. Client syntax and git diff --check passed. Release publish built net10.0 successfully; generated meta.json reports version 0.3.3.3, targetAbi 12.0.0.0, the existing plugin GUID and autoUpdate=true. Mobile guide activation screenshot reviewed. Explicit ServiceFilterAttribute orders (-900 native channels, -800 native actions) preserve the surrounding central access filter independently of registration order. Original source IDs, legacy fallback routes, normal client access and stable main remain unchanged.

Task 3 implementation notes: updated README with TV-remote selection/reset, ordinary Live TV navigation, extra Guide click, cached views and physical-device acceptance limits. Changelog Unreleased contains only 0.3.3.3-beta release notes; archived prior beta notes. Increased package version to 0.3.3.3 while preserving -beta informational version. Status: review (local build/docs); beta publication and hosted workflow/catalog verification next.

### 5. Beta publication and catalog verification — review

Task checked after local implementation review. Existing beta workflow researched above; publish the reviewed commit to beta using a non-force push, wait for Build/Beta release checks, then verify prerelease flag/tag/package version/checksum and separate beta/dev catalogs. Do not touch stable main or stable release/catalog. Hosted workflow outcome and hardware-acceptance limits will be recorded after publication.

Publication housekeeping note: the staged diff check detected one whitespace-only line in the newly added integration test (not yet tracked during the earlier local diff check). Remove that line in a follow-up formatting commit; no runtime/code behavior or package version changes. Recheck the staged diff before committing. The published runtime implementation remains commit 09c718e125dc4c8b8ec1d4e8e6a404440ad5a646.

Task 5 hosted verification and review: runtime Build 35381170594 and Beta release 35381170619 succeeded. Follow-up formatting Build 35381378328 succeeded. Published v0.3.3.3-beta is a non-draft prerelease. Downloaded ZIP MD5 cd1a266d7351e337f5410104cba53c83 matches beta catalog; SHA256 dda8d761d2e6a21fb109edec7fff4dd38a846ba63cc83c0031516643e94e3a17. DLL FileVersion 0.3.3.3, ProductVersion 0.3.3.3-beta+09c718e125dc4c8b8ec1d4e8e6a404440ad5a646; target ABI 12.0.0.0, plugin identity and auto-update preserved. Separate beta/dev catalogs contain the version. Stable release remains v0.3.3.1 and main remains 8033de45d56c0a77b4ca759deab0129c29fe408b. Status: review.

User acceptance: the user reports that 0.3.3.3 now works very well. This is their real-device feedback, alongside the recorded server/browser checks. They subsequently requested that the web "All visible groups" selection also activate a union on the TV; track that separately in docs/native-guide-beta-0.3.3.4.md.

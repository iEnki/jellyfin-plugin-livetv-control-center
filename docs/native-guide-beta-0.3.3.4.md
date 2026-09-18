# Beta 0.3.3.4: all visible groups on the native TV guide

## Requested behavior

The user confirms beta 0.3.3.3 works well on their TV. Extend the existing web "Use group on TV" action so it also accepts "All visible groups". The native guide must contain the union of permitted original channels from all currently visible groups, without duplicates. "All channels on TV" remains a distinct reset returning the complete ordinary authorized channel list, including channels outside groups.

Archon remains unused by the user's clarification. Track current task, research, doing, implementation notes and review here before taking the next task. Deliver through the existing beta-only workflow as 0.3.3.4; no stable changes.

## Research completed before code changes

- Web/client.js visible() excludes Preferences.HiddenGroupIds from the current authorized groups. updatePlayers and nativeGuide currently block state.group==='all', and PUT sends only GroupId.
- GroupService.GetGroups checks personal/shared-group visibility and current user permissions. GetAccessibleChannels applies native and central channel rights. ResolveChannels rechecks the current group and repairs source references. GroupGuideService.ResolveScope demonstrates union/distinct behavior with original IDs.
- NativeGuideScopes is an existing Dictionary<string,Guid> in the atomically cloned/saved UserGroups JSON. Preserve this schema and single-group scopes, rather than reinterpret Guid.Empty or introduce virtual groups. Add an explicit per-device visible-union mode separately; every write makes the modes mutually exclusive.
- NativeGuideChannelFilter already intersects original DTOs before pagination, revalidates after the action, restores absent parameters and keeps Programs unchanged. Extend its selection model while keeping these guarantees and compare-before-cleanup behavior.
- Existing beta release workflow triggers only when numeric project version changes on beta. It runs actual Jellyfin 12 API, .NET/web checks, publishes a prerelease and separate beta/dev catalogs. v0.3.3.3-beta is already published/verified; use the next numeric version for an installable update.

## Design

Persist a dynamic "all visible groups" mode for a user/device, independent of a single group ID. Resolve visibility using the user's stored HiddenGroupIds and current GetGroups rights on each native channel request, then union ResolveChannels over GetAccessibleChannels, deduplicating original channel IDs. Do not persist caller-supplied channel lists or treat this union as a reset. Added/changed groups and visibility/permission changes update the active union on the next guide fetch. If no visible accessible channels remain, invalidate the scope and fail open as in the existing beta.

Extend PUT with an explicit AllVisibleGroups=true selection and no GroupId. Reject mixed/missing selections. GET reports GroupId plus AllVisibleGroups and Enabled. Existing single-group requests/storage remain compatible. DELETE removes both modes for only the caller's device, including offline reset. Explicit native single-group folder selection replaces a previously applied union; native All channels reset clears either mode. No new native remote folders are needed for this web-requested extension.

Enable the existing action for state.group==='all' when at least one visible group exists and the authorized own-user official TV is available. Send the explicit union mode; show an English/German all-visible-groups confirmation. Keep device offline behavior, ordinary sender reset, remote playback, existing native group folders and web EPG unchanged.

## Tasks

### 1. Persistent union selection, API and native filter — review

Research above complete. Add explicit union selection/storage/read/write/reset, safe API input validation and fresh authorized visible-channel resolution. Preserve legacy single-group behavior, device isolation, pagination and fail-open behavior.

### 2. Web action and regression coverage — review

Check task 1 notes/review, research current player action and fixture request handling before edits. Enable all-visible action and localized confirmation. Test duplicates, hidden/denied groups, changing rights/membership, device/user isolation, storage reload, mode replacement, offline reset, API validation, native pagination and actual controller requests. Keep existing guide/action/playback tests green; browser-check activation and reset.

### 3. Documentation and beta publication — review

Check task 2 review, inspect existing docs/release workflow. Update version, README/changelog and plan; test/build Jellyfin 12/net10.0; publish through beta branch, wait for checks and verify prerelease, package/catalog checksums and unchanged stable. Record the physical-device acceptance limit for the newly added union; the previous version has positive user feedback.

## Sources and project evidence

The existing project code listed above and pinned Jellyfin API integration from source revision 6c073e19ddf604b2369c638716164fdab4c952dc. Previous client/navigation research remains documented in native-guide-beta-0.3.3.3.md. No new unstable client API or external service is introduced.

Task 1 implementation notes: retained NativeGuideScopes GUID schema and added NativeGuideVisibleGroupDevices. NativeGuideSelection distinguishes a single group from a dynamic visible-union mode; writes replace the alternate mode atomically. Reset clears both and stale cleanup compares the complete selection under the store lock. Union resolves current authorized/non-hidden groups through GetAccessibleChannels/ResolveChannels, deduplicates IDs and rechecks group visibility after resolution. Channel filter reads/revalidates the complete selection before/after native execution. PUT accepts AllVisibleGroups=true with no group ID and rejects mixed/missing mode. GET reports AllVisibleGroups; invalid/empty unions fail open and clear only the observed selection. Existing single-group native actions continue to replace/reset either mode. Status: review, validation covered in the next task.

Task 2 checked next. Existing visible(), button enablement, nativeGuide request, localization and browser fixture handling researched before web/test edits. Status: doing.


Task 2 implementation notes: web enables the existing action for all visible groups when a real eligible target and visible groups exist, sends only AllVisibleGroups:true (no arbitrary GUID/channel list), and shows localized union confirmation. No-group/offline targets remain disabled; reset remains available for remembered offline targets. Added server tests for duplicate/hidden/shared-denied channels, membership/rights updates, persistence/legacy JSON, mode replacement, stale cleanup, invalid/empty unions, concurrent reset, GET/PUT validation, own-user targets and other-client/device isolation. Extended the real LiveTvController integration for union pagination, hidden-group changes, no-page requests and reset restoring ungrouped channels. Native folder single-group/reset tests cover replacing a union. Added three DE/EN browser tests, including hidden groups, no visible groups, failed activation preserving prior scope, switching to single group and offline reset. All 43 Chromium web tests pass; screenshot of the new all-visible mode saved for visual review. Status: review after final server checks below.

Task 3 checked next, README/CHANGELOG/csproj and existing beta workflow researched before version/docs edits. Status: doing.

Final local checks: 204 .NET tests passed with the real supported Jellyfin API set (0 failures/skips). All 43 Chromium web tests passed; all-visible mobile screenshot reviewed. Package metadata test, client syntax and git diff --check passed. net10.0 Release publish succeeded; generated meta.json reports 0.3.3.4, target ABI 12.0.0.0 and autoUpdate=true. No dependency/ABI, native Programs, player transport, virtual channel or navigation API changes.

Task 3 implementation notes and review: updated README with dynamic visible union, hidden/denied exclusions, duplicate removal, distinct full reset and API flags/storage. Archived prior beta changelog; Unreleased contains only 0.3.3.4-beta. Updated numeric package version and web asset fallback version while retaining -beta and the established release workflow. Status: review (local implementation/docs).

### 4. Beta release and hosted verification — doing

Next task checked after implementation review. Existing beta-only workflow researched before publication. Publish the reviewed commit to beta with a non-force push; wait for both checks and verify non-draft prerelease, actual DLL version/ABI, ZIP/catalog checksum, separate beta/dev catalogs, stable release/main and clean working tree. Record exact hosted run/commit/package evidence. The new union has server/browser coverage; the user's positive TV acceptance applies to 0.3.3.3 until they try this update.

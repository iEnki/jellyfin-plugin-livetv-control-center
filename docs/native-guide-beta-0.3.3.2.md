# Beta 0.3.3.2: native Android TV / Fire TV guide

## Workflow and baseline

The user clarified on 2026-09-18 that Archon is not used and no MCP connection exists. This repository document replaces the requested Archon task tracking. For each phase: inspect the current task, research relevant documentation/examples, mark doing, implement, record implementation notes, mark review, then inspect the next phase. No additional service or paid component is required.

Baseline: main 8033de45d56c0a77b4ca759deab0129c29fe408b, Jellyfin.Controller/Model 12.0.0, net10.0. Existing .NET tests: 130 passed, 1 native-API test skipped pending an actual API assembly. beta currently points to 30a20306fba71b98f672f640511eb771d2570df0 (0.3.3.1-beta); stable 0.3.3.1 exists. Work starts from current main and is published through beta only.

## Architecture

- Register a scoped MVC action filter with PostConfigure<MvcOptions>, selecting only Jellyfin.Api.Controllers.LiveTvController.GetLiveTvChannels. Work on QueryResult<BaseItemDto>, never JSON streams.
- Authenticate using Jellyfin-UserId, Jellyfin-DeviceId, Jellyfin-Client and Jellyfin-IsApiKey claims. Require a real authenticated user, an explicit per-user/per-device scope and the official Jellyfin Android TV client name. Device display names do not establish client identity. API keys, missing claims, other clients and a differing queried user bypass the feature.
- Persist explicit scopes in a new NativeGuideScopes dictionary on the existing UserGroups document, independent of old ActiveGuideGroupId and web preferences. GroupStore already clones documents and saves atomically. No migration automatically activates filtering.
- On each request, re-read GetGroups, GetAccessibleChannels and ResolveChannels. Intersect group channels with the native response; never add items or bypass Jellyfin/channel-access rights. Deleted, denied or empty/unresolvable groups fall back to ordinary Live TV. Scope cleanup must compare the observed group ID to avoid deleting a concurrent new selection.
- Preserve original sort order, DTOs and all query filters. Jellyfin applies startIndex/limit before returning DTOs: for valid scopes, temporarily remove pagination from action arguments, filter the complete native result and then apply the original page with a corrected TotalRecordCount and StartIndex. If scope validation/post-processing fails, restore ordinary pagination over the unfiltered result. Core errors/cancellation must remain core errors/cancellation; never call the action twice or override error statuses.
- /LiveTv/Programs stays untouched. The stock TV client supplies channelIds obtained from its channel list. Original playback, recording IDs and streams are preserved. No virtual channels, ILiveTvService or client fork.
- Provide authenticated GET/PUT/DELETE scope endpoints for the current device and a selected target device. Reuse PlayerService discovery and official-client recognition. Initial beta targets only a device logged into the same user; remote-control permission for other users must not transfer personal groups. Reset must work while the target is offline. Setting a remote scope requires a currently eligible Android TV session.
- Optional DisplayContent navigation is secondary. Do not make successful scope activation depend on remote navigation, and do not promise direct timeline navigation. Prefer a clear action to apply/reset the selected group to the existing player target.

## Tasks

### 1. Plan and PoC — review

Research: exact server source from .github/scripts/test-native-api.cjs, existing ChannelAccessFilter/PluginServiceRegistrator, GroupService, PlayerService, native API test host and Microsoft MVC filter documentation. Implement explicit scope storage/service, narrow filter and authenticated endpoints. Validate a grouped response retains original channel IDs and that unscoped requests are identical. Notes: baseline research completed; pagination and interaction with existing security filter are explicit design requirements.

### 2. Reliability and permissions — review

Revalidate scopes per request; test deletion, shared-group revocation, channel revocation, rescan mapping, empty groups, fail-open exceptions, authentication exclusions, user/device isolation and persistence/reset. Add structured debug/warning logging without tokens. Check ordering against ChannelAccessFilter: its security behavior remains authoritative. Avoid new singleton dependency cycles. Review concurrency around scope replacement and cleanup.

### 3. User action and documentation — review

Expose apply-group and all-channels actions through the existing device selection where practical. Keep independent web guide and browsable native folders behavior. Document endpoint usage, same-user restriction, scope persistence, native channel-list effects, refresh/reopen requirements and the extra guide click. DisplayContent remains optional and may be deferred with an explicit explanation.

### 4. Integration and regression validation — review

Add focused filter/service/controller tests and a real Jellyfin-12 controller test using JELLYFIN_NATIVE_API. Cover complete-result filtering before pagination, correct totals, empty later pages, original sorting/options, unmodified Programs, no registration/startup cycle, old data remaining inactive, other clients, missing identity, API keys, denied targets and access-policy coexistence. Run all .NET tests, npm run test:native, web regression tests, syntax checks and packaging tests. Build/publish net10.0. Record exact results and distinguish device acceptance testing from server integration testing.

### 5. Beta packaging and release — review

Set numeric Version/AssemblyVersion/FileVersion to 0.3.3.2 and InformationalVersion to $(Version)-beta. Update README and CHANGELOG Unreleased for the existing beta release notes extractor. Commit validated changes and fast-forward/update beta without force pushing; recheck remote state first. Existing beta-release.yml publishes v0.3.3.2-beta and ZIP, updates beta-channel/manifest-beta.json and the existing dev catalog, and leaves stable manifest untouched. Monitor checks and verify the released package version, prerelease flag, ABI 12.0.0.0 and beta manifest checksum/source URL. Report changed files, architecture, tests, limitations and actual release URL.

## Acceptance criteria and known constraints

An explicitly scoped official TV device sees only original permitted channels in the selected group in its native channel list/guide. Another device/user/client remains unaffected. All channels reset and invalid scope return the normal authorized list. Programs needs no plugin filter. Pagination is correct after group filtering. Existing web/native fallback regressions pass. Installation preserves old settings and requires no new service. The release is an installable prerelease through the established beta catalog.

Group selection is outside the native guide. Channel-list/guide cache may require reopening the view/app. A direct jump into the timeline is not promised. This convenience scope is not a security boundary; existing Jellyfin and central channel-access policies enforce access. Physical Fire TV UI acceptance must be verified by the user if no device is available in this workspace.

## Primary sources

- Server controller: https://github.com/jellyfin/jellyfin/blob/6c073e19ddf604b2369c638716164fdab4c952dc/Jellyfin.Api/Controllers/LiveTvController.cs
- Claims creation: https://github.com/jellyfin/jellyfin/blob/6c073e19ddf604b2369c638716164fdab4c952dc/Jellyfin.Api/Auth/CustomAuthenticationHandler.cs
- Official TV channel/program requests: https://github.com/jellyfin/jellyfin-androidtv/blob/master/app/src/main/java/org/jellyfin/androidtv/ui/livetv/TvManagerHelper.kt
- MVC filter lifecycle: https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters?view=aspnetcore-10.0

PoC implementation notes: NativeGuideScopes persists with existing atomic user storage; a scoped PostConfigure MVC action filter intersects original DTOs before pagination; current-device and own-user TV target endpoints support set/read/reset. Existing suite still passes (130 + 1 pending native API). No controller action is retried on a core exception.


Reliability notes: 147 tests passed with the real Jellyfin-12 API, none skipped. Coverage includes per-device/user isolation, current identity/API-key exclusions, shared-group/channel revocation, invalid/empty scopes, offline reset, compare-before-cleanup, action races, single execution on core errors, and real native pagination/Programs requests. DisplayContent is deferred: activation remains independent of navigation; users open ordinary Live TV and select TV Guide. Browser plugin not available; existing Playwright regression suite is used for UI validation.


User-action notes: apply/reset actions reuse the existing player target without altering web-guide selection or remote playback. Both actions are localized. README includes beta installation, endpoint contracts, persisted scope semantics, same-user target requirement, offline reset, invalid-scope fallback and manual TV navigation.


Validation notes: 149 .NET tests passed including the exact Jellyfin-12 native API (none skipped); 38 browser regressions passed at desktop 1440x1000 and mobile 390x844, plus packaging/syntax checks. Native-guide-mobile screenshot inspected: actions and confirmation fit without horizontal overflow. Physical TV UI remains untested. Final packaging rechecks will run after setting the beta version.

Packaging notes: final 0.3.3.2 package rebuilt successfully; 149 .NET tests (including supported native API) and package metadata test passed after the version change. DLL file version is 0.3.3.2; informational version has -beta; meta.json uses ABI 12.0.0.0 and preserves plugin identity/auto-update. Publishing through beta-release.yml and post-release asset/catalog verification follow this review.

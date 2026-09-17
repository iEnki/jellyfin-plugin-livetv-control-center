# Remote Live TV development notes

Status: **review** (implementation complete; Robert's Fire TV hardware validation pending).

## Workflow

- Archon availability was checked before coding: no Archon tools/server were available in this session, and this repository has no Archon task metadata or AGENTS.md. No external task status was changed.
- Repository/release architecture and Jellyfin 12.0 session implementation were researched before coding.
- Local work progressed from analysis to **doing** on the isolated branch, then to **review** after implementation and validation.
- Branch: dev-remote-live-tv, based on main at 06fd7777e1e3443f735f8e9e0977b7845fbe098b.
- Existing main/dev branches, stable manifest.json, and stable release workflow were left untouched.

## Design and authorization

PlayerService enumerates ISessionManager.Sessions directly, avoiding GetSessions(ControllableByUserId)'s SupportsRemoteControl filter. It accepts a session only when it has a logged-in primary user, a DeviceId/SessionId, and an active session controller. SessionInfo.IsActive alone is insufficient because it returns true with no controllers.

Normal remote-capable clients remain eligible. The false-capability exception is restricted to the official client name Jellyfin Android TV (case-insensitive), which also runs on Fire TV. User-editable device names are not evidence. Capabilities and session controllers are never modified.

The server requires the caller's Live TV and media playback permissions and rejects disabled users. Discovery/play targets belong to the same primary user unless EnableRemoteControlOfOtherUsers permits cross-user control. Before sending, both users' Live TV/media playback permissions, accessible-channel queries and channel.GetPlayAccess == Full are checked again. Public sessions and API-key remote play are not supported.

The caller is resolved from the authenticated token through Jellyfin's session manager. Its primary UserId must match the authenticated user, and its SessionId must be nonempty. ISessionManager.SendPlayCommand receives that controlling SessionId plus a native PlayRequest(PlayNow, real LiveTvChannel Id). The privileged empty-controlling-session path is never used. This retains Jellyfin's native playback translation and authorization.

Targets are deduplicated by DeviceId, with the newest eligible session chosen deterministically. Every play re-resolves DeviceId against current sessions. No SessionId is persisted or exposed by the plugin discovery API.

Target preferences use the existing atomic per-user GroupStore, with a dedicated validated endpoint and server-owned device name. Ordinary/stale page preference writes preserve the stored target. The UI retains an offline preferred target rather than selecting local playback implicitly, and remote errors never fall back to phone playback. Discovery errors do not prevent loading the EPG.

## API

- GET /LiveTvGroups/Players: authorized, connected players; DeviceId, Name, Client, UsesAndroidTvDiscoveryFallback.
- PUT /LiveTvGroups/Players/Preference: { DeviceId: string | null }; null selects This device.
- POST /LiveTvGroups/Players/{deviceId}/Play/{channelId}: resolve session and send PlayNow. 204 means command sent; 403 means access denied; 409 indicates an unavailable target/session; transport errors return 502.

## Version and delivery

- Dev assembly/file/package version: **0.3.1.1**.
- Informational version: $(Version)-dev.remote-live-tv (+ SDK source revision when present).
- Required ordering: **0.3.1.0 < 0.3.1.1 < 0.3.2.0**; the actual published DLL version was inspected.
- A generated installation meta.json uses the existing plugin GUID/ABI, the project version, Active status and autoUpdate=true. This avoids stale metadata from replacing only the DLL, or Jellyfin disabling automatic updates for a folder-only installation. The instructions use a fresh versioned plugin directory and preserve per-user data.
- No stable or development release tag was pushed and no shared plugin catalog was changed.
- Branch CI now runs on dev-* pushes and uploads live-tv-groups-dev (DLL and installation meta.json at ZIP root) after successful tests/build.
- Test through that branch artifact or the locally supplied DLL ZIP. This is a manual test installation, not a new shared dev-catalog entry.
- Before releasing, change project Version to 0.3.2.0 and remove the dev InformationalVersion override. The higher stable version can then be offered normally by Jellyfin.
- The existing dev-release tag workflow requires commits reachable from origin/dev; it deliberately remains unchanged.

## Validation

- .NET Release tests: **59 passed**, none skipped.
- Browser fixture regression tests: **12 passed** on desktop 1440x1000 and mobile 390x844.
- Browser plugin not available; the repository's regular Playwright workflow was used with bundled Chromium and a local HTTP Jellyfin shell/API fixture.
- UI flow: independent groups EPG -> choose/remember Fire TV -> channel click -> remote device command and EPG remains open. Reload, offline retained target, failed command, failed preference save, explicit local selection and narrow mobile layout were exercised.
- Page identity/nonempty content, interaction outcomes, no runtime page errors, and mobile viewport overflow were checked. Local screenshots were saved outside the repository; the mobile selected-target screenshot was visually inspected.
- Package metadata integration test: **1 passed**; validates identity/version/update flags, generated JSON and an unchanged stable catalog.
- node --check client.js passed.
- Scoped dotnet format whitespace --verify-no-changes for new C# classes/tests passed.
- dotnet publish --configuration Release succeeded; plugin build had no compiler warnings/errors.
- git diff --check passed; final diff/manifest/version ordering reviewed.

## Hardware limitations and review checklist

- Requires Jellyfin 12.0 server ABI and an active official Android TV/Fire TV app in the foreground with its internal player.
- The server cannot reliably infer Android foreground state or the external-player preference. A connected session is not proof that Android's resumed WebSocket subscriber is handling commands.
- External player remote PlayNow is excluded from V1; a known upstream issue reports crashes in this configuration.
- A 204 response confirms the send path, not successful playback/stream opening. Hardware playback and actual tuner/transcoding behavior remain untested in this environment.
- No TV wake-up/app launch, pause/stop dashboard or remote hand-off from native program detail screens. Channel/logo and channel-card clicks are the remote actions.
- On Robert's device, verify normal Live TV first, then remote sender start/switch, remembered target after EPG reload, TV-app restart/new session, unavailable-target behavior, and user/channel permission revocation.
- Installation and detailed test steps are in README.md.

## Research sources

- Jellyfin 12.0 SessionManager: https://github.com/jellyfin/jellyfin/blob/v12.0/Emby.Server.Implementations/Session/SessionManager.cs (GetSessionToRemoteControl, SendPlayCommand, AssertCanControl, GetSessionByAuthenticationToken, GetSessions).
- Plugin installation metadata: https://github.com/jellyfin/jellyfin/blob/v12.0/Emby.Server.Implementations/Plugins/PluginManager.cs and https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Common/Plugins/PluginManifest.cs.
- SessionInfo: https://github.com/jellyfin/jellyfin/blob/v12.0/MediaBrowser.Controller/Session/SessionInfo.cs (IsActive, SupportsRemoteControl, session controllers).
- SessionController: https://github.com/jellyfin/jellyfin/blob/v12.0/Jellyfin.Api/Controllers/SessionController.cs.
- External-player upstream limitation: https://github.com/jellyfin/jellyfin-androidtv/issues/5731.
- Current Android TV playback launcher: https://github.com/jellyfin/jellyfin-androidtv/blob/master/app/src/main/java/org/jellyfin/androidtv/ui/playback/PlaybackLauncher.kt.

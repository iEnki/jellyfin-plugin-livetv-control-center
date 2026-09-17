# Localization and configurable display name

Status: review

Archon was not available in this session. Local workflow: inspect current stable branch, research official Jellyfin Web/server APIs, implement on dev-localization, validate, document and submit for review.

## Behavior

- English source interface strings and a shared embedded German dictionary cover viewing, management, preferences, device selection, administration, settings and native guide labels.
- The web interface reads Jellyfin Web's active document language, with its per-user local language preference and browser language as fallbacks. Jellyfin Web sets document.documentElement.lang when applying its display language. Language changes reload the plugin entry and rerender its own page.
- Other interface languages fall back to English. Client locales still format dates and times.
- Native guide requests use the client's Accept-Language, falling back to Jellyfin server UICulture. Clients that do not transmit language cannot supply their private display preference to the server.
- DisplayName is optional, trimmed, limited to 100 characters and excludes control characters. Empty restores the localized automatic default.
- The persistent shared channel/library name follows server UICulture; the web page follows client language. A custom name is shared across both, including the optional Plugin Pages shortcut after restart.
- The provider identity remains Live-TV Gruppen because Jellyfin derives channel IDs from provider names. The library's visible Name is updated separately. API entry lookup, playlists and media source ownership use the stable channel ID rather than a mutable display name.
- Name updates run after host startup and every 30 seconds. Only a changed name is saved. Other channel providers are not repeatedly enumerated; channel creation is requested only if the plugin root is absent.
- Native cache keys include language, and program metadata fingerprints include culture. User-created names and imported EPG text are not translated.
- README is public English user documentation, with English UI terms and language/name instructions. No development instructions were added to README.

## Validation

- 86 .NET tests passed, including default/custom/reset naming, native request/server language, language cache separation, native English guide, stable IDs during rename, renamed API lookup and embedded script resources.
- 22 Chromium browser tests passed, including German/English/fallback language, custom escaped title, unchanged user content, live language switching and administrator name save/reset.
- 1 packaging test passed; client/localization script syntax checks passed.
- Release configuration plugin publish succeeded; DLL and meta.json both report 0.3.2.1 with a dev informational marker.
- README anchors and local links checked; no German interface labels remain in README.

## Versioning and delivery

Development: 0.3.2.1 / v0.3.2.1-dev. This is higher than the published stable 0.3.2.0. The stable version intended to replace it must be strictly higher than 0.3.2.1 (for example 0.3.2.2 or 0.3.3.0), with no dev informational marker. No stable version has been chosen or published for this change.

Main and the existing stable release/catalog are preserved. Development publication uses the existing dev-channel repository.
## Stable promotion: 0.3.2.1

The user explicitly authorized publishing stable 0.3.2.1 on main. This supersedes the earlier plan to choose a strictly higher stable number. The dev marker is removed and packaging is stable. Version 0.3.2.0 upgrades normally; installed dev 0.3.2.1 requires manual stable reinstallation because the numeric version is equal. The stable changelog/release notes explain this migration. The public README remains English user documentation without development workflow details.

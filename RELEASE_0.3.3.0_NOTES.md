# Stable release 0.3.3.0

Status: review. Archon is unavailable; local workflow notes are used.

## Authorized scope

Promote current origin/beta 45b5dbf9fc9608f5736cf67d20a0dc6b9a4b3a9d to main as stable 0.3.3.0. Preserve rewritten remote history, use a regular fast-forward push, retain beta and both test catalogs, and do not implement the deferred channel access plan.

## Version and package

Set project/assembly/file/informational version to 0.3.3.0 without a beta marker. Update the client CSS fallback version, user README and changelog. Use the immutable stable tag for the catalog logo. The release workflow builds DLL, metadata and logo, publishes the stable catalog and refreshes beta/dev catalogs. Version 0.3.3.0 is greater than published beta 0.3.2.5.

## Validation and publication

Passed: 94 .NET tests, 32 Playwright/Chrome browser tests and 1 packaging test (127 total), Release publish, JavaScript syntax, English README links, translation coverage, workflow YAML/shell syntax and diff whitespace. Actual local DLL/file/metadata version is 0.3.3.0, without a beta/dev informational marker; GUID, ABI, automatic updates and packaged logo match. Publication verification completed successfully.

No authenticated Jellyfin server or Fire TV device is provided; actual hardware playback is not claimed. Robert previously confirmed the implemented playback works as desired.
## Verified publication

- Release commit: f3cd746db181f3ecd4c6c202cff6464978cc29e3; tag/release: v0.3.3.0, published stable and latest.
- GitHub Build 35331363272 and Release 35331366904 completed successfully.
- The workflow committed the stable catalog as b3cdf97 (Add version 0.3.3.0 to manifest).
- All three public repository URLs (stable latest manifest.json, beta-channel/manifest-beta.json and dev-channel/manifest-dev.json) offer stable numeric version 0.3.3.0 first, with matching plugin GUID, ABI 12.0.0.0, immutable source URL and checksum.
- Actual downloaded ZIP MD5: 6e9e0fbf48af2dcc55422302ee35a885. It contains exactly Jellyfin.Plugin.LiveTvGroups.dll, meta.json and Live-TV_Logo.png.
- Actual downloaded DLL assembly/file version is 0.3.3.0; informational version is 0.3.3.0+f3cd746db181f3ecd4c6c202cff6464978cc29e3. Metadata is stable, Active and autoUpdate=true. Packaged and public immutable catalog logos match the source asset.
- Beta branch remains 45b5dbf9fc9608f5736cf67d20a0dc6b9a4b3a9d; testing repository URLs remain available. Older preview entries are retired from the catalogs after stable promotion; old release assets remain available.
- Stable 0.3.3.0 is numerically higher than beta 0.3.2.5, allowing ordinary updates from both existing stable and beta installations. Future published betas must be newer than 0.3.3.0.
- Deferred CHANNEL_ACCESS_PLAN.md is preserved on main; no channel restriction feature was implemented in this release.

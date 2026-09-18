# Stable release 0.3.3.0

Status: review. Archon is unavailable; local workflow notes are used.

## Authorized scope

Promote current origin/beta 45b5dbf9fc9608f5736cf67d20a0dc6b9a4b3a9d to main as stable 0.3.3.0. Preserve rewritten remote history, use a regular fast-forward push, retain beta and both test catalogs, and do not implement the deferred channel access plan.

## Version and package

Set project/assembly/file/informational version to 0.3.3.0 without a beta marker. Update the client CSS fallback version, user README and changelog. Use the immutable stable tag for the catalog logo. The release workflow builds DLL, metadata and logo, publishes the stable catalog and refreshes beta/dev catalogs. Version 0.3.3.0 is greater than published beta 0.3.2.5.

## Validation and publication

Passed: 94 .NET tests, 32 Playwright/Chrome browser tests and 1 packaging test (127 total), Release publish, JavaScript syntax, English README links, translation coverage, workflow YAML/shell syntax and diff whitespace. Actual local DLL/file/metadata version is 0.3.3.0, without a beta/dev informational marker; GUID, ABI, automatic updates and packaged logo match. Publication verification is pending the regular main push and v0.3.3.0 tag workflow.

No authenticated Jellyfin server or Fire TV device is provided; actual hardware playback is not claimed. Robert previously confirmed the implemented playback works as desired.

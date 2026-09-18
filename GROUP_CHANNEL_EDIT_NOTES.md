# Direct editing of group channels

Status: review. Archon is unavailable; local workflow notes are used.

## Problem and change

Existing group cards offered rename/delete/order but no direct channel editing. The channel selector was reachable only in Channels view. Add Edit channels / Sender bearbeiten to group cards using the existing management permission gate.

The picker loads the clicked group's channel selection via the authorized group-specific Channels endpoint, independently of the current dropdown and previously loaded guide/channel scope. Keep saved order, append newly selected channels and leave other groups unchanged. User-switch responses and saves are ignored.

## Publication

Build numeric version 0.3.2.5, informational marker -beta. Publish through beta branch automation and verify both beta and legacy development repository catalogs. Preserve the current logo/package changes and rewritten remote history. Do not alter stable main or the stable release.

## Validation

- 94 .NET tests, 32 Playwright/Chrome browser tests and 1 packaging test passed (127 total).
- Build, JavaScript syntax, translation coverage, README links and diff whitespace passed.
- Desktop and mobile test fixtures verify direct editing, correct group selection despite a different dropdown group, saved order, adding/removing channels, central administrator versus ordinary user permissions, failed selection loading and cancellation.

## Verified publication

- Published beta v0.3.2.5-beta from a8eb1b18d24eb4f02e1d7bbf11c42c3722ce7e69. GitHub Build 35327144841 and Beta release 35327144794 succeeded.
- Public beta and legacy development catalogs offer 0.3.2.5 with matching source URL, checksum, GUID, ABI and DLL informational version. Existing packaged logo remains byte-identical.
- Stable main 3a3efda7af9adaaa5c62fa5583fc8a8cd2716653 and latest stable 0.3.2.1 remain unchanged.
- The next stable promotion must be numerically higher than the latest installed beta (currently at least 0.3.2.6).
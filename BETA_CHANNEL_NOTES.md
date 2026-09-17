# Installable beta channel

Status: review. Archon is unavailable; local workflow notes are used.

## Publishing policy

The user requires all development versions intended for testing to be installable directly from Jellyfin, not only as local ZIPs. Use the persistent beta branch and publish a GitHub pre-release plus both beta and existing development catalogs.

- Beta repository: https://github.com/iEnki/jellyfin-plugin-livetv-groups/releases/download/beta-channel/manifest-beta.json
- Persistent branch: beta. Publishing automatically creates immutable version tags vA.B.C.D-beta. Version and DLL/meta.json/catalog must match.
- First beta: 0.3.2.3-beta, numeric 0.3.2.3. It is newer than stable 0.3.2.1 and the manually supplied development build 0.3.2.2.
- Increase the four-part numeric version for every published build. A later stable release must be numerically newer than published test builds; for this beta the next stable must be at least 0.3.2.4.
- Beta publishing runs on beta pushes with a version increase; its generated tags are excluded from stable publishing. Stable main, stable catalog and stable release remain unchanged.
- The beta workflow runs .NET, browser and package checks before publishing. Existing development repository users receive the same beta build.
- When beta changes are later promoted to main, the stable workflow refreshes both testing catalogs and removes obsolete previews. No promotion is performed by this setup.

## Release procedure

Set a new four-part Version with the -beta InformationalVersion marker, update changelog and client cache fallback, run checks, commit and push beta, the workflow automatically builds and creates the matching vA.B.C.D-beta pre-release/tag. Documentation-only pushes do not publish another package. Wait for Beta release success and verify actual catalog sourceUrl, checksum, GUID, ABI, metadata and stable preservation.

## Verified first publication

- Implementation commit: a64194d9e94426656d8d06792f35693d77d55465; published pre-release: v0.3.2.3-beta.
- GitHub Build 35284201484 and Beta release 35284201420 succeeded. 94 .NET, 29 browser and 1 package tests passed (124 total).
- Public beta and legacy development catalog URLs both offer numeric version 0.3.2.3, matching immutable package URL, checksum, GUID and ABI 12.0.0.0.
- Actual downloaded ZIP includes the DLL plus meta.json, BETA metadata and automatic update support. DLL informational version is 0.3.2.3-beta+a64194d9e94426656d8d06792f35693d77d55465.
- Stable manifest and latest stable v0.3.2.1 are unchanged. Main remains e12a0b6bc57ddeeb56db2ff6472164491cfb245c.
- Real-device installation and Fire TV playback were not performed; no authenticated server/device connection was provided.
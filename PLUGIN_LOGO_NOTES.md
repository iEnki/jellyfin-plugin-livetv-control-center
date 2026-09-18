# Plugin logo

Status: review. Archon is unavailable; local workflow notes are used.

## Implementation

- Use the user-provided image unchanged as `assets/Live-TV_Logo.png`.
- Set catalog `imageUrl` to the public image on the persistent beta branch.
- Copy the logo beside the DLL and `meta.json`; set the installed manifest's `imagePath` to `Live-TV_Logo.png` so manual installations also have an image.
- Include the logo in Build artifacts and stable, development and beta ZIP creation.
- Allow testing catalogs to inherit branding from the publishing branch while retaining stable versions from main.
- Publish numeric version 0.3.2.4 with the beta marker; update the web cache fallback to the matching version. A subsequent stable release must use at least 0.3.2.5.
- Document extraction of all three installation files.

## Validation

- 94 .NET tests, 29 browser tests and the package metadata test pass (124 total); client and metadata script syntax checks pass.
- Local Release build and ZIP contain DLL version 0.3.2.4, matching metadata with the relative image path, and the byte-identical original logo.
- GitHub Build 35290731339 and Beta release 35290731307 succeeded for implementation commit df3fc5e. Published pre-release: `v0.3.2.4-beta`.
- Downloaded release ZIP contains all three files, matching version/identity metadata, automatic update support and the byte-identical logo. Asset SHA256 and both catalog checksums match the actual ZIP.
- Beta and existing development catalogs both offer 0.3.2.4 and reference the publicly reachable logo. Existing stable versions, download URLs and checksums, and main/dev branches are preserved.
- Next task: verify the plugin image in the Jellyfin dashboard after installing the new beta; no authenticated server session is available here.

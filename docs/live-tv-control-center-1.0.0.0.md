# Live-TV Control Center 1.0.0.0 repository migration

## Scope and compatibility

Create a new public repository `iEnki/jellyfin-plugin-livetv-control-center` from the accepted `main` history. Release stable 1.0.0.0 and create a beta branch for later numeric betas. Preserve plugin GUID `7b3792b4-b988-4ec5-b9e5-1a952a652b83`, internal assembly/namespace/API route names, persisted data and the channel provider identity so the existing installation can update without losing groups, settings, permissions or native-guide scopes. The old repository remains available for historical releases. The visible name, catalog, documents, package names and new repository links become Live-TV Control Center.

## Task 1 — research: reviewed; implementation: review

Reviewed source plugin entry point, localized display-name service, manifest, ZIP metadata writer, packaging tests and Stable/Beta workflows. Jellyfin requires a four-part numeric version; user-facing 1.0 is 1.0.0.0. The old provider string `Live-TV Gruppen` is a fixed channel ID input and must not change. A new beta branch must begin at the released stable baseline; future beta version commits must use a number above the latest stable release and a `-beta` informational suffix. On branch creation beta publishing should be skipped.

Update branding and version while preserving runtime compatibility; make README explain migration and both channels. Test existing integration/browser suites and packaging with the new branding.

## Task 2 — publication: review

After Task 1 review: create the public new repository, push main, tag/release v1.0.0.0 through the Stable workflow, verify its ZIP/metadata/catalog, create beta branch from the updated stable main, and initialize its beta catalog for future releases. Document hosted results.

Implementation notes: renamed the runtime/dashboard/catalog/default localized brand, source repository links, npm package and release ZIP filenames to Live-TV Control Center; set stable version 1.0.0.0. The generated logo uses the new wordmark and is packaged as Live-TV_Control_Center_Logo.png, while the historical logo remains in source. The internal assembly/API routes/provider ID and GUID are preserved. README documents old-repository migration, custom display-name retention and archive screenshots. Beta workflow skips the push event that only creates the beta branch. Local results: 204 .NET tests (zero skipped) with the pinned real Jellyfin 12 API, 43 browser tests, metadata packaging test and client syntax checks passed; local 1.0.0.0 publish/meta contain the new catalog name/logo, ABI 12, autoUpdate=true and the original GUID. Task 1 ready for review; Task 2 authorized and in progress.

Hosted verification: public repository https://github.com/iEnki/jellyfin-plugin-livetv-control-center was created from the already-public source history. Stable tag v1.0.0.0 points to reviewed commit 5508beabab6b50f9603632bff5aec9c1ba206a9f. Build run 35436423887 and Release run 35436433546 succeeded. The version release is GitHub latest, neither draft nor prerelease. Official ZIP live-tv-control-center_1.0.0.0.zip contains DLL, meta.json and Live-TV_Control_Center_Logo.png. ProductVersion 1.0.0.0+5508beabab6b50f9603632bff5aec9c1ba206a9f, plugin GUID unchanged, ABI 12.0.0.0, autoUpdate=true. MD5 459d3fbed07a385762857ef68953b6ac matches stable and beta catalogs; ZIP SHA256 d1084e5c8b146863ed1ab4db9ed297bf74aa3f9d0bb82c5ba2b88067caeffeb2 matches GitHub. The manifest bot added version 1.0.0.0 on main. Beta branch and beta-channel prerelease were created from that stable baseline. Downloaded manifest-beta.json offers 1.0.0.0 and has the same checksum; stable latest remains v1.0.0.0. Both tasks are ready for review; all requested publication work is complete.

# Stable release 0.4.0.0

User acceptance: the owner confirmed that beta 0.3.3.4 works as desired and explicitly requested promotion to the main stable release 0.4.0.0. Archon is not used; tasks and implementation notes are maintained here.

## Task 1: promotion preparation — review

Research: release.yml accepts stable v* tags only when their commit belongs to origin/main. It checks a matching four-part project version and rejects beta/dev informational versions. It runs native API, .NET, packaging and Chromium checks; publishes DLL/meta/logo; updates the stable manifest on main and refreshes beta/dev catalogs. The stable metadata writer retains plugin identity and autoUpdate. origin/main is an ancestor of the accepted origin/beta; no divergence or uncommitted changes exists. Version/tag 0.4.0.0 does not exist.

Implementation: promote the accepted beta commits to main, update project/client version to 0.4.0.0 with stable informational metadata, and document the final native-guide behavior and limitations in README/CHANGELOG. Keep tested runtime behavior and stored user/device selections unchanged.

Validation: run all existing .NET tests against the pinned Jellyfin 12 API, browser tests, syntax and packaging checks, and publish a local stable package to inspect metadata.

## Task 2: publication — doing

After task 1 is reviewed, commit and push main, then create v0.4.0.0 to invoke the existing stable release workflow. Verify successful CI, a non-prerelease latest release, official ZIP metadata/checksum, and stable/beta/dev catalog entries. Record hosted verification and provide the stable release link.

Implementation notes: main fast-forwarded to the accepted beta without changing runtime behavior. Project and client fallback versions are 0.4.0.0; InformationalVersion no longer has a beta suffix. README describes stable installation and owner acceptance; CHANGELOG has an exact 0.4.0.0 section while retaining historical beta notes. All 204 .NET tests passed (zero skipped) with the pinned real Jellyfin API, all 43 Chromium tests passed, the packaging test and both syntax checks passed. Release publish succeeded; local DLL FileVersion is 0.4.0.0 and ProductVersion has no prerelease marker. meta.json retains GUID, ABI 12.0.0.0, logo and autoUpdate=true, with stable changelog branding.

Task 2 research/check: existing stable workflow and latest stable repository URL reviewed; release/tag v0.4.0.0 absent. Publication is explicitly authorized by the owner. Hosted verification remains pending.

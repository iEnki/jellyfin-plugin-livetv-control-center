# Stable release 0.4.0.0

User acceptance: the owner confirmed that beta 0.3.3.4 works as desired and explicitly requested promotion to the main stable release 0.4.0.0. Archon is not used; tasks and implementation notes are maintained here.

## Task 1: promotion preparation — review

Research: release.yml accepts stable v* tags only when their commit belongs to origin/main. It checks a matching four-part project version and rejects beta/dev informational versions. It runs native API, .NET, packaging and Chromium checks; publishes DLL/meta/logo; updates the stable manifest on main and refreshes beta/dev catalogs. The stable metadata writer retains plugin identity and autoUpdate. origin/main is an ancestor of the accepted origin/beta; no divergence or uncommitted changes exists. Version/tag 0.4.0.0 does not exist.

Implementation: promote the accepted beta commits to main, update project/client version to 0.4.0.0 with stable informational metadata, and document the final native-guide behavior and limitations in README/CHANGELOG. Keep tested runtime behavior and stored user/device selections unchanged.

Validation: run all existing .NET tests against the pinned Jellyfin 12 API, browser tests, syntax and packaging checks, and publish a local stable package to inspect metadata.

## Task 2: publication — review

After task 1 is reviewed, commit and push main, then create v0.4.0.0 to invoke the existing stable release workflow. Verify successful CI, a non-prerelease latest release, official ZIP metadata/checksum, and stable/beta/dev catalog entries. Record hosted verification and provide the stable release link.

Implementation notes: main fast-forwarded to the accepted beta without changing runtime behavior. Project and client fallback versions are 0.4.0.0; InformationalVersion no longer has a beta suffix. README describes stable installation and owner acceptance; CHANGELOG has an exact 0.4.0.0 section while retaining historical beta notes. All 204 .NET tests passed (zero skipped) with the pinned real Jellyfin API, all 43 Chromium tests passed, the packaging test and both syntax checks passed. Release publish succeeded; local DLL FileVersion is 0.4.0.0 and ProductVersion has no prerelease marker. meta.json retains GUID, ABI 12.0.0.0, logo and autoUpdate=true, with stable changelog branding.

Task 2 research/check: existing stable workflow and latest stable repository URL reviewed; release/tag v0.4.0.0 absent. Publication is explicitly authorized by the owner. Hosted verification completed below.

Hosted implementation/validation notes: main release commit ff326423d90b79250882fe386e5f8e88a2bb3389 was pushed and tagged v0.4.0.0. Build run 35404373151 and stable Release run 35404380159 both succeeded. GitHub latest is v0.4.0.0, neither draft nor prerelease. Official ZIP contains only plugin DLL, meta.json and logo; ProductVersion is 0.4.0.0+ff326423d90b79250882fe386e5f8e88a2bb3389 without beta/dev suffix. Identity, ABI and automatic updates are retained. MD5 491b0023051f87acfe283371c01f7c85 matches the stable, beta and development catalogs, all now offer stable 0.4.0.0 first. SHA256 aa37eb8bbb33725cd6309d859763a26a3a7a3b729779d0bc6f2bd63e458d3c96 matches GitHub's asset digest. The workflow's manifest commit e734d84 was fast-forwarded locally. Release: https://github.com/iEnki/jellyfin-plugin-livetv-groups/releases/tag/v0.4.0.0

Both tasks are ready for review; requested stable publication and verification are complete. The tag remains on the validated runtime commit; this subsequent documentation-only commit records hosted results.

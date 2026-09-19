# Repository Guidelines

## Project Structure & Module Organization

The Jellyfin 12 plugin lives in `src/Jellyfin.Plugin.LiveTvGroups/`. `Api/` contains controllers and response filters; `Services/`, `Channel/`, and `Storage/` handle groups, playback, native guide scopes, and persistence. `Web/` holds the injected page, CSS, and JavaScript; `Localization/strings.json` supplies English/German text. C# xUnit tests are in `tests/Jellyfin.Plugin.LiveTvGroups.Tests/`; browser and packaging tests are in `tests/web/` and `tests/packaging/`. Keep logos and screenshots in `assets/`, release automation in `.github/`, and user documentation in `README.md` and `CHANGELOG.md`.

## Build, Test, and Development Commands

Use .NET 10 and Node.js 24. Run `dotnet test --configuration Release` for the C# suite, and `dotnet publish src/Jellyfin.Plugin.LiveTvGroups --configuration Release --output out` to build the installable DLL. Run `npm ci` and `npx playwright install chromium` before `npm run test:web`; `npm run test:packaging` checks generated `meta.json` and the logo. `npm run test:native` checks integration against the supported Jellyfin API. Check changed browser scripts with `node --check src/Jellyfin.Plugin.LiveTvGroups/Web/client.js`.

## Coding Style & Naming Conventions

Follow nearby code: four-space C# indentation, PascalCase public types/members, camelCase JavaScript variables, and nullable C# references. The project treats compiler warnings as errors; keep changes warning-free. Keep the existing `Jellyfin.Plugin.LiveTvGroups` assembly/namespace, plugin GUID, API routes, and `GroupsChannel.ChannelName`: they preserve upgrades and channel identity despite the Live-TV Control Center display name. Use English source keys with German translations for new user-facing text.

## Testing Guidelines

Name C# test files `*Tests.cs` and browser tests `*.test.cjs`. Add focused regressions for changed group permissions, user/device isolation, native guide filtering, or playback. For web changes, cover visible behavior in the Playwright fixture and both languages where text changes. Run relevant tests locally; CI repeats the full suites and packaging checks.

## Commits & Pull Requests

Use short, imperative commits such as `Clarify Jellyfin compatibility`. PRs should explain the behavior change, compatibility impact, and tests run; link an issue when relevant and include screenshots for visible UI changes. Stable releases use four-part versions on `main` and a matching `vX.Y.Z.W` tag. Future beta builds come from `beta`, require a higher numeric version and `-beta` informational suffix, and publish through the separate beta catalog.

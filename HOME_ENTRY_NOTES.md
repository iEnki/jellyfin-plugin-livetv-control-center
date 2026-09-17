# Home entry option

Status: review. Archon is not available in this session; workflow notes are kept here.

## Implementation

- Global administrator setting, disabled by default; authenticated Entry response requires enabled integrations and a user-accessible plugin channel.
- Presentation-only web adaptation of My Media cards and library buttons in the same container. Original links and permissions are preserved; no LiveTv endpoints or server view responses are intercepted.
- Native Smart TV/Android TV/Fire TV clients are unaffected. Old asynchronous Entry responses cannot cross a user or language change.
- Browser plugin not available; use the repository's existing Playwright browser regression suite with installed Chrome.

## Versioning

- Development branch: dev-home-entry. Numeric build version: 0.3.2.2; informational version: 0.3.2.2-dev.home.
- Stable main and release 0.3.2.1 are unchanged. No release tag or catalog update is created for this change.
- Any later stable promotion must use a higher numeric version than an installed development build; do not repeat the equal-version development/stable transition.

## Hardware validation

No authenticated Jellyfin test-server connection or Fire TV device has been provided for hardware validation in this session. Browser tests cover target selection and two channel commands using the test server; actual TV playback remains a manual acceptance check.

## Validation

- .NET: 94 passed. Playwright/Chrome: 29 passed. Packaging: 1 passed. Total: 124 passed.
- Release-configuration publish succeeded; JavaScript syntax, translation coverage, English README links/anchors and diff whitespace passed.
- Desktop 1440×900 and mobile 390×844 home fixtures show only the groups entry, while ordinary menu and recording links remain present. No page errors or unexpected browser console errors in the new home flows.
- Actual Fire TV playback and a full production Jellyfin home page are not exercised by the browser fixtures.

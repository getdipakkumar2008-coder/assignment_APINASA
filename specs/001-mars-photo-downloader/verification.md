# Verification Record

Evidence for the acceptance criteria. Everything here was observed on 2026-10-06 on the author's Windows 11 machine with the project NASA key in the git-ignored `.env`. The key is never printed or stored in this file.

## Live behaviour of the photo sources

| Check | Result |
|---|---|
| Mars Rover Photos service (`api.nasa.gov/mars-photos/...`) | HTTP 404 "No such app" for every route, rover and date |
| Provider chain | Falls back automatically; summary shows `fallback: MarsRoverPhotos: HTTP 404 (service not found)` |
| Fallback photos for `02/27/17`, `June 2, 2018`, `Jul-13-2016` | 5 real JPEG photos each (sols 1621, 2069, 1398), 15 files, all valid JPEG signatures |
| `April 31, 2018` | `InvalidDate`, "Day 31 does not exist in April 2018", no folder created |
| Second run | `0 downloaded, 15 skipped`, files untouched (AC-5) |

Opt-in live test: `RUN_LIVE_TESTS=1 dotnet test --filter Category=Live` passes (3 dates, 3-5 photos each, idempotent second run).

## Console runner (AC-1, AC-7)

`dotnet run --project src/MarsPhotos.Cli` from the repo root and from `src/MarsPhotos.Cli` both produce the same summary and exit code 0 (path resolution against the solution root).

## API and UI (User Story 6)

- API on `http://localhost:5180`: `POST /api/runs` returned the summary in 1.5-2.2 s (three consecutive runs); `/photos/...` served `image/jpeg`; `/photos/../dates.txt` returned 404; CORS header present only for `http://localhost:4200`; the key never appeared in the API log.
- One earlier run took 50 s while NASA's CDN was slow; it was not reproducible (CLI 3.4 s, API 1.5-2.3 s afterwards). Timeouts and retries bound such stalls (60 s per request).
- Angular dev server on `http://localhost:4200` with the proxy: run, latest summary and photos all work through `/api` and `/photos`.
- A headless Chrome screenshot of the real page is in [`docs/ui-results.png`](../../docs/ui-results.png): totals, three successful cards with source, fallback reason, counts and five thumbnails each, and the invalid-date card with its error code.

## Automated checks

| Suite | Result |
|---|---|
| `dotnet test` unit tests (`MarsPhotos.Tests`) | 347 passed, 1 skipped (the opt-in live test) |
| `dotnet test` API tests (`MarsPhotos.Api.Tests`) | 44 passed |
| Angular tests (`npm test`, Vitest + jsdom) | 42 passed, including 6 axe-core accessibility scenarios |
| `npm audit --omit=dev` | 0 vulnerabilities |
| Negative checks | Removing image alt text makes the axe test fail with `image-alt (critical)`; planting a fake key in the repo makes `RepoSecretScanTests` fail and was then removed |

## Accessibility (FR-041)

Automated: axe-core (WCAG 2.0/2.1 A and AA, best practices) reports no violations in the empty, running, mixed-results, no-photos and error states. Colour contrast cannot be computed in jsdom, so it was **not** machine-checked: the palette uses dark text on light backgrounds chosen for at least 4.5:1, and this still needs a human check with a contrast tool.

**Not yet done by a human (outstanding):** keyboard-only walkthrough in a real browser and a screen-reader pass (Narrator/NVDA). The automated tests assert focusable controls, labelled landmarks, a skip link, `aria-live` status and alt text, but they are not a substitute.

## Open items

- Final acceptance (AC-1..AC-12 walkthrough on a clean clone), performance note for SC-008, and the security/code reviews are recorded in the polish phase.

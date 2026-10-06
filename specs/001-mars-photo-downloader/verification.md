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

## Final acceptance walkthrough (AC-1 to AC-12)

Run on 2026-10-06 after the review fixes, including a **fresh `git clone` into an empty folder with no `.env`**.

| AC | Result | Evidence |
|---|---|---|
| AC-1 builds and runs locally | **Pass** | Fresh clone: `dotnet build` 0 warnings / 0 errors; all tests green without a key; `npm ci && npm test && npm run build` pass; CLI without a key refuses with exit 1 and tells the user how to supply one; with the key the CLI, API, UI and Docker image all ran |
| AC-2 dates parsed correctly | **Pass** | `02/27/17`, `June 2, 2018`, `Jul-13-2016` valid; `April 31, 2018` invalid with "Day 31 does not exist in April 2018"; parser tests under four cultures |
| AC-3 valid dates use the API, 3-5 photos | **Pass (via fallback)** | Primary returns 404 (observed live), chain falls back; 5 photos per date; live smoke test |
| AC-4 photos stored per date | **Pass** | `photos/2017-02-27/`, `2018-06-02/`, `2016-07-13/` with 5 valid JPEGs each; no folder for April 31 |
| AC-5 no re-download | **Pass** | Second run `0 downloaded, 15 skipped`; `IdempotencyTests` check timestamps and call counts |
| AC-6 failures handled | **Pass** | Tests for 5xx/429/404/HTML/bad JSON, timeouts, dropped connections, oversized and non-image bodies, partial failure, cancellation; no `.part` or zero-byte files remain |
| AC-7 output summary | **Pass** | Console table, JSON and UI show date, downloaded/skipped/failed counts and errors per line plus totals |
| AC-8 no secrets | **Pass** | Absent from tracked files and history (`check-no-secrets` .sh and .ps1), logs, responses, UI, image history; guard tests proven to fail when a key is planted |
| AC-9 README and AI_NOTES | **Pass** | `README.md` (run steps, assumptions, limitations), `AI_NOTES.md` (tools, prompts, real defects and fixes, changes) |
| AC-10 many small commits | **Pass** | 38 commits on the feature branch, each a single concern, tests committed with the code they verify |
| AC-11 tech constraints | **Pass** | `net8.0` (pinned by `global.json`), Angular 22 UI, async I/O end to end with `CancellationToken`; no `async void`, no blocking on tasks; key never hardcoded |
| AC-12 bonus | **Pass** | Unit and API tests; error handling and logging; UI with thumbnails; Dockerfile built and run |

Success criteria SC-001 to SC-010 are covered by the same evidence; SC-008 (a run under 30 s) measured 1.5-3.4 s on a normal connection.

**Honest limits of this verification:** keyboard-only and screen-reader checks and a colour-contrast tool pass are still to be done by a person; the fallback source is an undocumented public feed; the Claude-in-Chrome extension was not connected, so the UI was checked by headless Chrome plus automated tests rather than an interactive session.

## Open items

- Code and security review: see `review.md`. Remaining: human keyboard / screen-reader / contrast check of the UI.

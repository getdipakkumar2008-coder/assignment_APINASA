# Phase 0 Research: Mars Rover Photo Downloader

All items are resolved; no `NEEDS CLARIFICATION` remains; decision D-1 is approved.
Evidence marked *(live, 2026-10-06)* came from probing the real services with the project key (kept in the gitignored `.env`).

## R-1 Primary service availability
- **Decision**: Keep the Mars Rover Photos service as first-choice provider, implemented exactly to the contract in REQ §8.1, but expect it to be `Unavailable`.
- **Evidence** *(live)*: every route under `https://api.nasa.gov/mars-photos/api/v1/` (photos by `earth_date`/`sol`, `latest_photos`, `manifests`, `rovers`) for all three rovers returns **HTTP 404, `text/html`, "No such app"**. The community mirror returns the same.
- **Rationale**: the brief mandates it, it may return, and the adapter is small. The failure mode (HTML 404) is itself a required test (NFR-15).
- **Alternatives**: build only the fallback (rejected: violates the brief); build only the primary (rejected: delivers nothing today).

## R-2 Fallback source
- **Decision**: NASA raw-image feed `GET https://mars.nasa.gov/rss/api/?feed=raw_images&category=msl&feedtype=json&sol={sol}` (Curiosity only, no key needed).
- **Evidence** *(live)*: JSON with `images[]` each having `imageid`, `sol`, `date_taken_utc`, `date_received`, `camera.instrument`, `image_files{small,medium,large,full_res}`.
- **Image hosts**: `mars.nasa.gov` and `mars.jpl.nasa.gov`. Both serve `image/jpeg` over **HTTPS** with `Content-Length` (small ≈ 4.7 KB, medium ≈ 35 KB, full_res ≈ 105 KB, large ≈ 145 KB for the sampled image).
- **Decision**: rewrite `http://` image URLs to `https://`; allow-list exactly `mars.nasa.gov` and `mars.jpl.nasa.gov` (plus `*.nasa.gov` is *not* accepted — exact hosts only).
- **Risk**: undocumented, may change (R-2 in REQ). Mitigation: contract fixture tests, tolerant DTOs, failure → `API_BAD_RESPONSE`.

## R-3 What "earth_date" means → **Decision D-1 (needs approval)**
- **Finding** *(live)*: filtering by `date_taken_utc` equal to the requested date gives 25 / 0 / 187 photos for 2017-02-27 / **2018-06-02** / 2016-07-13. For 2018-06-02 the rover captured nothing on that UTC date; sol 2069 (the sol whose Earth date is 2018-06-02) holds 60 photos taken 2018-06-01 UTC and sols 2070 starts 2018-06-03.
- **Cause**: the original service assigns each sol an Earth date; `earth_date=D` returns photos **of the sol assigned to D**, not photos stamped D.
- **Decision**: fallback maps date → sol and returns that sol's photos. Sol(D) = floor((D at 12:00 UTC − 2012-08-06T05:17:57Z) / 88 775.244 s). Verified: 2017-02-27 → 1621, 2018-06-02 → 2069, 2016-07-13 → 1398, each returning photos.
- **Rationale**: preserves original semantics; satisfies SC-002 and the brief's folder example for 2018-06-02.
- **Alternatives**: (a) `date_taken_utc` filter — rejected, 0 photos for one sample date; (b) ±1 sol union filtered by capture date — same defect; (c) union of neighbouring sols — returns photos not belonging to the date.
- **Approved by the user 2026-10-06**; REQ FR-3.13 and spec FR-011 / US5 scenario 3 amended.

## R-4 Mission ranges (FR-007)
- **Decision**: a small static table in Domain: Curiosity landed 2012-08-06, active (end = today UTC); Opportunity 2004-01-25 → 2018-06-10 (last contact); Spirit 2004-01-04 → 2010-03-22 (last contact). A date outside `[landing, end]` is `OutsideMissionRange` with **no** provider call.
- **Rationale**: static data avoids a network call, which would defeat the purpose of skipping the lookup. The table is configuration-overridable.
- **Note**: Opportunity/Spirit ranges apply to the primary only; with the primary down they yield `SourceUnavailable`.

## R-5 Date parsing (FR-003–FR-006)
- **Decision**: `DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)` with formats: `MM/dd/yy`, `M/d/yy`, `MM/dd/yyyy`, `M/d/yyyy`, `MMMM d, yyyy`, `MMM d, yyyy`, `MMM-dd-yyyy`, `MMM-d-yyyy`, `MMMM-d-yyyy`, `yyyy-MM-dd`, `d MMMM yyyy`.
- **Two-digit year**: invariant culture's calendar `TwoDigitYearMax` is 2029 → `17` ⇒ 2017; to be machine-independent the parser explicitly clones `InvariantCulture` and sets `TwoDigitYearMax = 2099` (so 00–99 ⇒ 2000–2099).
- **Invalid vs unrecognized**: if text matches a *shape* regex (`name/number tokens`) but `TryParseExact` fails, a second pass checks day-in-month to emit `DATE_INVALID` ("Day 31 does not exist in April 2018"); otherwise `DATE_UNRECOGNIZED`.
- **Alternatives**: `DateTime.Parse` (culture/machine dependent, rejected); NodaTime or Humanizer (extra dependency, rejected, YAGNI).

## R-6 Resilience (FR-012, FR-013)
- **Decision**: `Microsoft.Extensions.Http.Resilience` pipeline per named client: total timeout, per-attempt timeout, retry (3 attempts, exponential backoff + jitter) with a custom `ShouldHandle` = {timeout, `HttpRequestException`, 408, 429, ≥500}; 400/401/403/404 never retried; `Retry-After` honored via delay generator. Circuit breaker disabled (tiny workload).
- **Rationale**: first-party, DI-friendly, replaces hand-rolled loops. Polly v8 under the hood.
- **Primary-vs-fallback rule**: 404/HTML → primary `Unavailable` immediately (no retry), then the chain falls back.

## R-7 Idempotent storage (FR-014–FR-017)
- **Decision**: path = `{root}/{yyyy-MM-dd}/{stem}{ext}`; stem sanitized to `[A-Za-z0-9._-]`; ext from allow-list; verify resolved full path stays inside the root; skip when file exists and length > 0; download into `{name}.part` (`FileMode.Create`, so stale parts are overwritten), validate `Content-Type: image/*` and length ≤ 25 MB, then `File.Move(overwrite:false)`; on any failure delete the `.part`.
- **Alternative**: content hashing (deferred to roadmap; filenames from provider ids suffice).

## R-8 Configuration and secrets (FR-024–FR-026)
- **Decision**: bind `Nasa` section; key from `NASA__ApiKey`. A dev-only loader reads `.env` in the working directory/solution root into process environment variables before the host builds. User-secrets works unchanged. `ValidateOnStart` fails with an actionable message when the key is missing. No demo-key fallback (clarification).
- **Logging redaction**: provider clients log the request *path* and a redacted URL (`api_key=***`); built-in `HttpClient` logging categories are set to `Warning` so URLs with keys aren't logged at `Information`.
- **Alternative**: DotNetEnv package (rejected: one more dependency for 20 lines).

## R-9 Host topology
- **Decision**: Api (minimal APIs, `ProblemDetails`, static files for the photos root only, CORS for `http://localhost:4200`) and Cli (generic host, console renderer). Both call `ProcessDatesUseCase`. The API runs the use case synchronously per `POST /api/runs` (workload is tiny) and returns the `RunSummary`; `GET /api/runs/latest` returns the cached one.
- **Alternatives**: background job + SignalR (rejected: unjustified for ≤30 s runs); single combined host with a `--cli` switch (rejected: blurs responsibilities).

## R-10 Angular
- **Decision**: Angular CLI 22.x, standalone components, strict TS, signals for state, `HttpClient` service, dev-server proxy to the API, lazy `loading="lazy"` thumbnails with alt text, one route. Default test runner generated by the CLI.
- **Accessibility**: semantic table/list, status text (not colour-only), focus-visible, `aria-live` for run status.

## R-11 Exit codes (OQ-7, unanswered → default retained)
- `0` run completed; `1` fatal (missing key, unreadable input, unwritable output); `2` unexpected crash.

## Resolved unknowns
| Unknown | Resolution |
|---|---|
| Is the primary API usable? | No, fallback required (R-1) |
| Do image URLs support HTTPS? | Yes (R-2) |
| How to select fallback photos for a date | Sol-based, D-1 (R-3) |
| Two-digit-year safety | Explicit `TwoDigitYearMax` (R-5) |
| Secret loading without new packages | Small `.env` loader (R-8) |

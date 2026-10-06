---
description: "Task list for Mars Rover Photo Downloader"
---

# Tasks: Mars Rover Photo Downloader

**Input**: Design documents from `/specs/001-mars-photo-downloader/` (plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md)

**Prerequisites**: spec.md, plan.md (both present and approved incl. decision D-1), constitution v1.0.0

**Tests**: REQUIRED (spec FR-028, constitution X). Logic with clear contracts is tests-first (red, then green).

**Organization**: Grouped by user story. Foundational work precedes all stories.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1..US7 map to spec user stories. IDs like `FR-015`, `SC-003` are **spec.md** IDs; `AC-n` are REQ §14 criteria.
- Each task names a verifying test (`Test:`) or manual check where code is not testable.
- **Commit points** are marked `COMMIT:`; commit only when the preceding tasks build and ALL tests pass. The pre-commit hook enforces secret scan, build and test, so a failing (red) test is never committed alone: write the test first, make it pass, then commit test and implementation together in one small commit (`feat: ...` with its tests).
- **SECRET RULE**: no task writes, echoes or commits the NASA key. The key lives only in the gitignored `.env`; tests use a fake key such as `test-key-0000`.

## Path Conventions

`src/MarsPhotos.{Domain,Application,Infrastructure,Api,Cli}`, `src/marsphotos-ui`, `tests/MarsPhotos.Tests`, `tests/MarsPhotos.Api.Tests`, repo-root files.

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 Create `global.json` at repo root pinning SDK `8.0.204` with `rollForward: latestFeature` (so `dotnet` uses 8.x though SDK 10 is installed). Test: `dotnet --version` prints 8.0.x. [Plan §Technical Context]
- [ ] T002 Create `MarsPhotos.sln` and class-library/host projects with `dotnet new` targeting `net8.0`: `src/MarsPhotos.Domain`, `src/MarsPhotos.Application`, `src/MarsPhotos.Infrastructure`, `src/MarsPhotos.Api` (web, minimal API), `src/MarsPhotos.Cli` (console), `tests/MarsPhotos.Tests` (xUnit), `tests/MarsPhotos.Api.Tests` (xUnit); add project references inward only (Application→Domain, Infrastructure→Application, Api/Cli→Infrastructure, tests→projects). Test: `dotnet build` succeeds. [Constitution III]
- [ ] T003 [P] Create `Directory.Build.props` (Nullable enable, ImplicitUsings, TreatWarningsAsErrors true, AnalysisLevel latest, LangVersion 12) and `.editorconfig` (C# naming/formatting rules). Test: `dotnet build` clean and `dotnet format --verify-no-changes` passes. [Constitution XII]
- [ ] T004 [P] Create `dates.txt` with exactly the four lines `02/27/17`, `June 2, 2018`, `Jul-13-2016`, `April 31, 2018` (trailing spaces trimmed is fine, no BOM). Test: file read in T033 test. [FR-001, DEL-2]
- [ ] T005 [P] Verify `.env.example` exists with placeholder `NASA__ApiKey=YOUR_NASA_API_KEY` and `.env`, `photos/`, `bin/`, `obj/` are in `.gitignore`; add `appsettings.Development.json` ignore only if it could hold secrets. Test: `git check-ignore .env photos` lists both; `git ls-files .env` empty. [FR-024, SEC-1, SEC-2, SC-007]
- [ ] T006 [P] Add package references: `Microsoft.Extensions.Http.Resilience` 8.x to Infrastructure; `Microsoft.AspNetCore.Mvc.Testing` 8.x to Api.Tests; (no other third-party packages). Test: `dotnet restore`. [Plan §Primary Dependencies, Constitution II]

**COMMIT**: `chore: scaffold .NET 8 solution, build props and sample dates file`

---

## Phase 2: Foundational (Blocking Prerequisites)

**CRITICAL**: no user story work begins until this phase is complete.

### Domain (no dependencies)

- [ ] T007 [P] Create `src/MarsPhotos.Domain/ErrorCodes.cs` with string constants: `DATE_UNRECOGNIZED`, `DATE_INVALID`, `DATE_OUT_OF_RANGE`, `DATE_DUPLICATE`, `API_NO_PHOTOS`, `API_RATE_LIMITED`, `API_AUTH`, `API_UNAVAILABLE`, `API_BAD_RESPONSE`, `DL_FAILED`, `DL_REJECTED_URL`, `DL_TOO_LARGE`, `IO_ERROR`, `CONFIG_MISSING_KEY`, `INPUT_FILE_ERROR`, `RUN_IN_PROGRESS`. Test: reflection test asserts the exact set. [FR-021, data-model §catalogue]
- [ ] T008 [P] Create records/enums in `src/MarsPhotos.Domain/`: `DateInputLine(LineNumber, RawText)`, `DateStatus {Valid, Invalid, Unrecognized}`, `ParsedDate`, `RoverPhoto(Id, Sol?, Camera, ImageUrl, EarthDate, Rover)`, `PhotoStatus {Downloaded, Skipped, Failed}`, `PhotoOutcome`, `DateResultStatus {Success, NoPhotos, InvalidDate, UnrecognizedDate, OutsideMissionRange, DuplicateDate, SourceUnavailable, PartialFailure, Failed}`, `ErrorInfo(Code, Message, Detail?)`, `DateResult` (derived Downloaded/Skipped/Failed counts), `RunSummary` + `RunTotals`. `RawText` max 200 chars noted in XML doc. Test: `DateResult` derived counts. [data-model §Entities, FR-019, FR-036]
- [ ] T009 [P] Create `src/MarsPhotos.Domain/MissionRange.cs`: static table Curiosity 2012-08-06→today(UTC), Opportunity 2004-01-25→2018-06-10, Spirit 2004-01-04→2010-03-22, inclusive; `Contains(rover, date, todayUtc)`; unknown rover → false. Write the test FIRST (red) in `tests/MarsPhotos.Tests/Domain/MissionRangeTests.cs`, then implement (green): boundary dates, future date, pre-landing, unknown rover. [FR-007, FR-032, AC-2]

### Application ports (depend on Domain)

- [ ] T010 [P] Create ports in `src/MarsPhotos.Application/Abstractions/`: `IDateSource` (reads lines), `IPhotoProvider` + `ProviderResult` (`Photos(selected, available)` / `NoPhotos` / `Unavailable(code, message, retriable)`), `IPhotoDownloader`, `IPhotoStore` (`Exists(date,fileName)`, `WriteAsync(...)`), `IClock` (UTC now). All async methods take `CancellationToken`. Test: compile + used by later tests. [contracts/photo-provider.md, Constitution VI]
- [ ] T011 [P] Create `src/MarsPhotos.Application/RunOptions.cs` and `NasaOptions.cs` per data-model §Configuration: defaults and ranges quoted verbatim (PhotosPerDate 3–5 default 5; MaxRetries 0–5 default 3; MaxParallelDownloads 1–5 default 3; LookupTimeoutSeconds 30; DownloadTimeoutSeconds 60; MaxImageBytes 26214400; AllowedImageHosts `mars.nasa.gov`,`mars.jpl.nasa.gov`; InputFile `dates.txt`; OutputRoot `photos`; Rover `curiosity`). Test: `OptionsValidationTests` rejects out-of-range values and missing/short key (`< 8` chars). [FR-024, FR-025, FR-008]
- [ ] T012 Create `.env` loader `src/MarsPhotos.Infrastructure/Configuration/DotEnvLoader.cs`: reads `KEY=VALUE` lines from `.env` in the working dir (or ancestor up to the .git root), ignores comments/blank lines, sets process environment variables only if not already set, never logs values. Test `DotEnvLoaderTests`: parsing, no override of existing env, missing file is a no-op, value never appears in thrown/logged text (use `test-key-0000`). [FR-024, SEC-1, SEC-10]
- [ ] T013 Create `src/MarsPhotos.Infrastructure/Configuration/ServiceCollectionExtensions.cs` `AddMarsPhotos(IConfiguration)`: bind `Nasa`/`Run` sections, `ValidateOnStart`, actionable `CONFIG_MISSING_KEY` message telling the operator to set `NASA__ApiKey` in `.env`/environment/user-secrets; no demo-key fallback. Test: host build without key fails with that message and with no key text. [FR-025, FR-024, AC-8]
- [ ] T014 [P] Create `src/MarsPhotos.Infrastructure/Security/UrlPolicy.cs`: accepts only `https` (rewrites `http` to `https` first), exact allow-listed host, no userinfo, default port; returns `DL_REJECTED_URL` reason otherwise. Write the test FIRST (red) in `UrlPolicyTests`, then implement (green): allowed hosts, `evil.com`, `mars.nasa.gov.evil.com`, `https://user@mars.nasa.gov`, `file:`, port 8443, http rewrite. [FR-018, FR-034, SEC-3, SEC-4]
- [ ] T015 [P] Create `src/MarsPhotos.Infrastructure/Logging/RedactingHelper.cs` (replace `api_key=...` in any URL/string with `api_key=***`) and set `System.Net.Http.HttpClient` log categories to `Warning` in config defaults. Test `RedactionTests`: query-string key variants, case-insensitive, unaffected other text. [FR-026, SEC-1, NFR-9]
- [ ] T016 Create `src/MarsPhotos.Application/FileNames.cs`: sanitize provider id to `[A-Za-z0-9._-]`, collapse others to `_`, max 100 chars, extension allow-list `.jpg/.jpeg/.png` (default `.jpg`), and `EnsureInside(root, path)` rejecting traversal. Write the test FIRST (red) in `FileNameTests`, then implement (green): `../../x`, drive letters, reserved names (`CON`), unicode, empty stem. [FR-014, FR-018, SEC-3]
- [ ] T017 [P] Write `tests/MarsPhotos.Tests/Providers/RetryPolicyTests.cs` using a scripted `HttpMessageHandler`: 500,500,200 → success after retries; 429 with `Retry-After: 1` honored; 404/403/400 never retried (single call); timeout then success; exhausted retries → typed failure; total attempts ≤ 4; backoff delays doubling from 1 s capped at 8 s with jitter (use injectable `TimeProvider`/delay so tests run fast). [FR-012, FR-033, AC-6]
- [ ] T018 Implement `src/MarsPhotos.Infrastructure/Http/ResilienceExtensions.cs`: `AddStandardPhotoResilience` using `Microsoft.Extensions.Http.Resilience` — per-attempt and total timeouts from options, retry predicate {timeout, `HttpRequestException`, 408, 429, ≥500}, exponential backoff + jitter, `Retry-After` honored, circuit breaker disabled. Makes T017 pass. [FR-012, FR-013, FR-033, D-8]

**Checkpoint**: Domain, ports, options, URL policy, file-name safety, redaction ready.

**COMMIT** (as separate small commits): `feat: add domain model and error codes` · `feat: add mission ranges` · `feat: add options, env loader and secure configuration validation` · `feat: add URL policy, redaction and safe file names` · `feat: add HTTP resilience pipeline`

---

## Phase 3: User Story 1 - Turn a list of dates into saved rover photos (Priority: P1) MVP core

**Goal**: dates file → parse → look up → download → files in `photos/<date>/`.

**Independent Test**: with a fake provider and fake HTTP, the four sample lines produce files for three dates and none for April 31.

### Tests first (red)

- [ ] T019 [P] [US1] Write `tests/MarsPhotos.Tests/Parsing/DateLineParserTests.cs` (must fail first): `02/27/17`→2017-02-27, `June 2, 2018`→2018-06-02, `Jul-13-2016`→2016-07-13, `April 31, 2018`→`Invalid` with message "Day 31 does not exist in April 2018", `2/29/2016` valid, `2/29/2017` invalid, `13/01/17` unrecognized/invalid, `  june 2, 2018  ` valid, empty/`abc`/`99/99/99` no exception, line >200 chars unrecognized with limit message, runs identically under `de-DE` and `fr-FR` current culture. Tag `[Trait("AC","AC-2")]`. [FR-003, FR-004, FR-005, FR-029, SC-001]
- [ ] T020 [P] [US1] Write `tests/MarsPhotos.Tests/Parsing/DateFileReaderTests.cs`: reads `dates.txt` fixture; trims; skips blank lines but keeps line numbers; handles CRLF/LF and BOM; missing file → `INPUT_FILE_ERROR`; empty file → `INPUT_FILE_ERROR` message; >1000 non-blank lines → excess reported as unrecognized limit error. [FR-001, FR-002, FR-029, AC-2]
- [ ] T021 [P] [US1] Write `tests/MarsPhotos.Tests/Storage/FileSystemPhotoStoreTests.cs`: new photo written under `<root>/2017-02-27/<name>`; no `.part` remains; write failure (read-only dir / stream throws) leaves no partial; traversal name rejected. Also assert FR-038: an existing valid file is never overwritten or deleted by a write to the same name (second write is rejected/skipped) and no code path deletes photo files. [FR-014, FR-016, FR-017, FR-038, AC-4]
- [ ] T022 [P] [US1] Write `tests/MarsPhotos.Tests/Processing/ProcessDatesUseCaseTests.cs` (fakes for provider/downloader/store): four sample lines → 3 valid results with 5 downloaded each, 1 `InvalidDate`, order preserved, totals equal sum of lines; out-of-range date makes zero provider calls; duplicate date processed once with `DuplicateDate` noted; all-invalid list returns full summary with zero provider calls; fewer than N photos is `Success` with `PhotosAvailable`. Tag `AC-2/AC-3/AC-4/AC-7`. [FR-006, FR-007, FR-008, FR-009, FR-030, FR-039, SC-001, SC-002, SC-006]

### Implementation (green)

- [ ] T023 [US1] Implement `src/MarsPhotos.Application/Parsing/DateLineParser.cs` per research R-5: cloned invariant culture with `TwoDigitYearMax = 2099`, `DateOnly.TryParseExact` with the documented format array (`MM/dd/yy`, `M/d/yy`, `MM/dd/yyyy`, `M/d/yyyy`, `MMMM d, yyyy`, `MMM d, yyyy`, `MMM-dd-yyyy`, `MMM-d-yyyy`, `MMMM-d-yyyy`, `yyyy-MM-dd`, `d MMMM yyyy`), shape-matching second pass to emit `DATE_INVALID` with day-in-month message vs `DATE_UNRECOGNIZED`; no exceptions for control flow. Makes T019 pass. [FR-003–FR-006, NFR-1]
- [ ] T024 [US1] Implement `src/MarsPhotos.Infrastructure/Input/FileDateSource.cs` (`IDateSource`) async line reading with limits (1000 lines, 200 chars). Makes T020 pass. [FR-001, FR-002, FR-029]
- [ ] T025 [US1] Implement `src/MarsPhotos.Infrastructure/Storage/FileSystemPhotoStore.cs`: path `{root}/{yyyy-MM-dd}/{name}`, `EnsureInside`, write to `{name}.part` (`FileMode.Create`) then `File.Move` without overwrite, delete `.part` in `finally` on failure, `Exists` = exists and length > 0, create directories lazily, `IO_ERROR` mapping. Makes T021 pass. [FR-014–FR-017, FR-016, NFR-3, R-7]
- [ ] T026 [US1] Implement `src/MarsPhotos.Application/Processing/ProcessDatesUseCase.cs`: read lines → parse → dedupe by date → mission-range check (no provider call when outside) → provider lookup → select first N by ordinal photo id → download with `MaxParallelDownloads` bounded concurrency → per-photo isolation → `DateResult` status derivation (data-model) → `RunSummary`; sequential over dates; cooperative cancellation; injected `IClock`. Makes T022 pass. [FR-005–FR-009, FR-013, FR-017, FR-030, FR-039, D-5, D-10]
- [ ] T027 [US1] Implement `src/MarsPhotos.Infrastructure/Download/HttpPhotoDownloader.cs`: named `HttpClient`, `UrlPolicy` check, redirect handling only to allow-listed hosts (max 3), require `image/*`, enforce `MaxImageBytes` while streaming (`DL_TOO_LARGE`), reject zero-byte, stream into store. Test `tests/MarsPhotos.Tests/Download/HttpPhotoDownloaderTests.cs` with fake handler: success, non-image content type, HTML-with-200, oversized, redirect to evil host, zero bytes, rejected host → `DL_REJECTED_URL`. [FR-017, FR-018, FR-035, SEC-4, SEC-6]
- [ ] T028 [US1] Register the Infrastructure implementations that exist so far (store, downloader, date source, clock, options) in `src/MarsPhotos.Infrastructure/Configuration/ServiceCollectionExtensions.cs` `AddMarsPhotos`, with `HttpClient` timeouts from options. Provider registration and the full `ProcessDatesUseCase` resolution test move to the end of US5 (see the provider-chain registration task). Test: `ServiceProviderTests` resolves `IPhotoStore`, `IPhotoDownloader`, `IDateSource`. [Constitution III]

**Checkpoint**: US1 logic is complete against fakes; real providers come in US5.

**COMMIT**: `feat: implement date parsing and validation (with tests)` · `feat: add file date source` · `feat: add filesystem photo store with atomic writes` · `feat: add photo downloader with URL policy` · `feat: add processing use case`

---

## Phase 4: User Story 2 - Understand exactly what happened (Priority: P1)

**Goal**: console summary + JSON with date, counts, errors, totals; no secrets.

**Independent Test**: run the use case over a mixed list; the rendered table lists each line once and totals add up.

- [ ] T029 [P] [US2] Write `tests/MarsPhotos.Tests/Output/ConsoleSummaryRendererTests.cs`: one row per input line (input, date or `-`, status, source, New/Skip/Fail, notes with `CODE: message`), totals line equals sums (lines/valid/invalid/downloaded/skipped/failed), `April 31, 2018` row shows "Day 31 does not exist in April 2018", fixture containing `test-key-0000` in an error detail is never printed. Tag `AC-7`. [FR-019, FR-021, FR-036, SC-006, SC-007]
- [ ] T030 [P] [US2] Write `tests/MarsPhotos.Tests/Output/RunSummaryJsonTests.cs`: serialization matches `contracts/openapi.yaml` `RunSummary` (camelCase, enum names as strings, relative photo `url`, null `date` for invalid). [FR-020, contracts §openapi]
- [ ] T031 [US2] Implement `src/MarsPhotos.Application/Output/ConsoleSummaryRenderer.cs` and `RunSummaryJson.cs` (System.Text.Json options shared by CLI and API). Makes T029/T030 pass. [FR-019, FR-020]
- [ ] T032 [US2] Implement `src/MarsPhotos.Cli/Program.cs`: load `.env`, generic host, `AddMarsPhotos`, args `--file --rover --count --out --json`, run use case, render table (or JSON to stdout, logs to stderr), exit codes per `contracts/cli.md` (0 completed; 1 fatal config/input/output/arguments; 2 unexpected; 130 cancelled via Ctrl+C `CancellationToken`). Test `tests/MarsPhotos.Tests/Cli/CliExitCodeTests.cs` using the program's testable `RunAsync(args, services)`: missing key→1, missing file→1, completed with failed date→0, bad `--count 9`→1, cancelled→130. [FR-022, FR-025, AC-1, AC-7]

**COMMIT**: `feat: add console summary and JSON output` · `feat: add CLI runner with exit codes`

---

## Phase 5: User Story 3 - Re-run safely without repeated downloads (Priority: P1)

**Goal**: idempotent re-runs; partial downloads never look complete.

**Independent Test**: run twice against fakes; second run downloads 0 and does not touch files.

- [ ] T033 [P] [US3] Write `tests/MarsPhotos.Tests/Processing/IdempotencyTests.cs` using a real temp-dir store and a counting fake downloader: second run → 0 downloader calls, `Skipped` = first run's total, file timestamps unchanged (`LastWriteTimeUtc`); deleting one file → exactly one download; zero-byte existing file → re-downloaded; stale `.part` file → ignored/overwritten; adding one new date → only its photos downloaded. Tag `AC-5`. [FR-015, FR-016, FR-036, NFR-3, SC-003, AC-5]
- [ ] T034 [US3] Adjust `ProcessDatesUseCase`/`FileSystemPhotoStore` so existence checks occur before any HTTP request, skipped photos produce `PhotoStatus.Skipped`, and `.part` files are never treated as existing (fixes anything T033 exposes; no behaviour change if already green). [FR-015, FR-016]
- [ ] T035 [P] [US3] Add `tests/MarsPhotos.Tests/Storage/CrashSimulationTests.cs`: downloader stream throws mid-copy → no final file, `.part` removed; cancellation mid-copy → same. [FR-016, AC-6, SC-005]

**COMMIT**: `feat: prevent duplicate downloads (with idempotency and crash-safety tests)`

---

## Phase 6: User Story 4 - Keep working when things go wrong (Priority: P1)

**Goal**: per-date and per-photo failures are isolated and reported, and no secret reaches any log. (Retry/timeout policy is the foundational T017–T018.)

**Independent Test**: simulated 429/5xx/timeout/404/HTML/invalid JSON/one failing photo → run completes with clear errors.

- [ ] T036 [P] [US4] Write `tests/MarsPhotos.Tests/Processing/FailureIsolationTests.cs`: one photo download fails → others saved, date `PartialFailure`, error listed `DL_FAILED`; all fail → `Failed`; provider `Unavailable` for date 2 → date 1 and 3 still `Success`; unexpected exception inside one date's processing is caught → that date `Failed` with `IO_ERROR`/generic code, others continue; unwritable root → fatal `IO_ERROR` surfaced. Tag `AC-6`. [FR-017, NFR-2, SC-005, FR-022]
- [ ] T037 [US4] Harden `ProcessDatesUseCase` per T036 (per-date `try/catch` for non-cancellation exceptions, per-photo isolation, cancellation rethrown), and add structured logging with date/photo/attempt scopes (never keys). [FR-017, FR-026, NFR-9]
- [ ] T038 [P] [US4] Add `tests/MarsPhotos.Tests/Logging/NoSecretInLogsTests.cs`: run the full use case with a capturing logger and key `test-key-0000` configured; assert no log line, exception message or summary contains it, and request logging shows `api_key=***` only. Tag `AC-8`. [FR-026, SEC-1, SC-007]

**COMMIT**: `feat: isolate per-date and per-photo failures` · `feat: add no-secret-in-logs tests`

---

## Phase 7: User Story 5 - Photos while the original service is unavailable (Priority: P2, required for live results)

**Goal**: primary client per brief, working raw-feed fallback, automatic chain.

**Independent Test**: with the primary returning the recorded HTML 404, the three sample dates still yield photos via the fallback fixtures and the summary names `RawImageFeed`.

### Fixtures and tests first

- [ ] T039 [P] [US5] Record sanitized fixtures in `tests/MarsPhotos.Tests/Fixtures/`: `primary_photos_ok.json` (hand-written per REQ §8.1), `primary_empty.json`, `primary_no_such_app.html` (the live "No such app" page), `rawfeed_sol1621.json`, `rawfeed_sol2069.json`, `rawfeed_sol1398.json` (trimmed to ~10 images each from live responses, no keys). Test: fixtures deserialize in T040/T041 tests. [NFR-15, F-2, F-4]
- [ ] T040 [P] [US5] Write `tests/MarsPhotos.Tests/Providers/SolConverterTests.cs`: 2017-02-27→1621, 2018-06-02→2069, 2016-07-13→1398; landing day→sol 0; noon-UTC reference; date before landing rejected. Tag `D-1`. [FR-011, FR-031, SC-004]
- [ ] T041 [P] [US5] Write `tests/MarsPhotos.Tests/Providers/MarsRoverPhotosProviderTests.cs` against fixtures/scripted handler: OK JSON→`Photos` sorted by id and truncated to N with `available`; `{"photos":[]}`→`NoPhotos`; HTML 404→`Unavailable(API_UNAVAILABLE)` with exactly one request; 200 HTML/invalid JSON/missing `photos`→`API_BAD_RESPONSE`; 403→`API_AUTH`; 429 exhausted→`API_RATE_LIMITED`; request URL contains `earth_date` and the key but the logged URL is redacted; unknown fields tolerated. [FR-008, FR-009, FR-012, contracts §photo-provider]
- [ ] T042 [P] [US5] Write `tests/MarsPhotos.Tests/Providers/RawImageFeedProviderTests.cs`: request URL uses computed sol and no key; maps `imageid`→Id, `full_res` else `large`, `http`→`https`, `EarthDate` = requested date; sorted by `imageid` ordinal, first N; empty `images`→`NoPhotos`; non-curiosity rover→`Unavailable` "fallback supports Curiosity only"; malformed/HTML→`API_BAD_RESPONSE`; all three sample fixtures yield ≥ 3 photos. Tag `SC-004`. [FR-010, FR-011, FR-030, FR-031, D-1]
- [ ] T043 [P] [US5] Write `tests/MarsPhotos.Tests/Providers/ProviderChainTests.cs`: primary `Photos` stops chain (fallback not called); `NoPhotos` stops chain (no fallback); `Unavailable` → fallback called and `FallbackReason` recorded; both unavailable → `SourceUnavailable`/`API_UNAVAILABLE`; provider name recorded in result; fallback logged at Warning with date and reason. [FR-010, FR-040, D-2, D-3]

### Implementation

- [ ] T044 [US5] Implement `src/MarsPhotos.Domain/SolConverter.cs` per research R-3: `floor((date@12:00Z − 2012-08-06T05:17:57Z).TotalSeconds / 88775.244)`. Makes T040 pass. [FR-011, FR-031]
- [ ] T045 [US5] Implement `src/MarsPhotos.Infrastructure/Providers/MarsRoverPhotosProvider.cs` + DTOs (tolerant) + typed `HttpClient` registration with resilience. Makes T041 pass. [FR-008–FR-012, FR-033]
- [ ] T046 [US5] Implement `src/MarsPhotos.Infrastructure/Providers/RawImageFeedProvider.cs` + DTOs + typed `HttpClient`. Makes T042 pass. [FR-010, FR-011, FR-030]
- [ ] T047 [US5] Implement `src/MarsPhotos.Application/Providers/ProviderChain.cs` (registration happens in the registration task below); surface provider and fallback reason in `DateResult`. Makes T043 pass. [FR-010, FR-040]
- [ ] T048 [US5] Implement quota awareness in both provider clients: read the `X-RateLimit-Remaining` response header (when present) and log a Warning with date and remaining count when fewer than 100 requests remain; never fail the request on it. Test `tests/MarsPhotos.Tests/Providers/RateLimitWarningTests.cs` with scripted headers (`99` warns, `100`/missing does not; no key in the log). [FR-040, REQ FR-3.9, Constitution VII]
- [ ] T049 [US5] Register `MarsRoverPhotosProvider`, `RawImageFeedProvider` and `ProviderChain` in `AddMarsPhotos`, and add `tests/MarsPhotos.Tests/Configuration/ServiceProviderTests.cs` case asserting `ProcessDatesUseCase` resolves with the fake key `test-key-0000`. [Constitution III, FR-010]
- [ ] T050 [US5] Opt-in live smoke test `tests/MarsPhotos.Tests/Live/LiveSmokeTests.cs` guarded by env `RUN_LIVE_TESTS=1` and `NASA__ApiKey` present (skipped by default; never prints the key): sample dates yield ≥ 3 photos from the real fallback. Manual run recorded in quickstart. [SC-004, AC-3]

**COMMIT**: `feat: add sol conversion` · `feat: add Mars Rover Photos provider` · `feat: add raw image feed fallback provider` · `feat: chain providers with automatic fallback`

---

## Phase 8: User Story 7 - Configure without exposing secrets (Priority: P2)

**Goal**: operator config works end-to-end; no key leakage anywhere.

**Independent Test**: no key → clear refusal; with key → nothing in repo/logs/outputs.

- [ ] T051 [P] [US7] Write `tests/MarsPhotos.Tests/Configuration/ConfigurationPrecedenceTests.cs`: CLI args > environment > appsettings; `.env` populates environment but never overrides an existing env var; invalid ranges rejected with named setting; missing key message mentions `NASA__ApiKey` and `.env` and does not mention any key value. [FR-024, FR-025, AC-8]
- [ ] T052 [US7] Create `src/MarsPhotos.Cli/appsettings.json` and `src/MarsPhotos.Api/appsettings.json` with non-secret defaults only (no `ApiKey` property); wire `.env` loading in both hosts before host build; add `Properties/launchSettings.json` without secrets (API on `http://localhost:5080`). Test: `RepoSecretScanTests` scans the working tree (excluding `.env`) for the value of `NASA__ApiKey` from the environment when set and for 40-char key-like assignments. [FR-024, SEC-1, SEC-2, SC-007]
- [ ] T053 [US7] Add `.claude`-independent repository check script `scripts/check-no-secrets.ps1` and `scripts/check-no-secrets.sh` that reads the key from `.env`, runs `git grep -nF` over tracked files and history (`git log -p -S`), prints only `clean` or file names (never the key). Test: run both; expect `clean`. [SC-007, AC-8]

**COMMIT**: `feat: finalize configuration precedence and secret scans`

---

## Phase 9: User Story 6 - Browse results visually (Priority: P2, in scope)

**Goal**: API host plus Angular page with thumbnails, states and WCAG 2.1 AA behaviour.

**Independent Test**: API integration tests with stub providers; Angular component tests for all states; manual keyboard/screen-reader pass.

### API (tests first)

- [ ] T054 [P] [US6] Write `tests/MarsPhotos.Api.Tests/RunsEndpointTests.cs` with `WebApplicationFactory<Program>` and stub `IPhotoProvider`/fake HTTP: `POST /api/runs` (no body) returns 200 `RunSummary` matching schema for the sample dates; with `dates` body works; >100 dates or line >200 chars → 400 `application/problem+json`; bad rover → 400; missing key → 500 problem `CONFIG_MISSING_KEY` without key text; `GET /api/runs/latest` 404 before a run and 200 after; concurrent second `POST` while running → 409 "run in progress"; `GET /health` 200. [FR-020, FR-037, contracts §openapi, AC-7]
- [ ] T055 [P] [US6] Write `tests/MarsPhotos.Api.Tests/StaticPhotosTests.cs`: `/photos/2017-02-27/<file>` serves a stored image; `../` traversal and files outside the photos root → 404; CORS allows only `http://localhost:4200` (preflight from another origin gets no allow header); responses contain no key. [FR-023, SEC-7, D-13]
- [ ] T056 [US6] Implement `src/MarsPhotos.Api/Program.cs` minimal API: DI via `AddMarsPhotos`, `ProblemDetails` + global exception handler, `POST /api/runs` (single-run lock via `SemaphoreSlim(1)` → 409), `GET /api/runs/latest` (in-memory), `/health`, static files mapped only to the configured photos root at `/photos`, CORS policy for `http://localhost:4200`, request-size and date-count limits, cancellation via `HttpContext.RequestAborted`; make `Program` public partial for tests. Makes T054/T055 pass. [FR-020, FR-023, FR-037, FR-042, SEC-7]
- [ ] T057 [US6] Add cancel support: `POST /api/runs/cancel` (or DELETE) cancels the active run and the latest summary marks unfinished dates `Cancelled`; `Cancelled` is already defined in data-model.md and `contracts/openapi.yaml`; implement it in `DateResultStatus` and the use case. Test in `RunsEndpointTests`. [FR-042, contracts §openapi]

**COMMIT**: `feat: add API host with run endpoints and static photos` · `feat: add run cancel`

### Angular

- [ ] T058 [US6] Scaffold the SPA: `npx @angular/cli@latest new marsphotos-ui --directory src/marsphotos-ui --style=css --routing=false --ssr=false --skip-git --strict --defaults`; confirm standalone components and TypeScript `strict`; add `proxy.conf.json` proxying `/api` and `/photos` to `http://localhost:5080` and `npm start` script using it. Test: `npm --prefix src/marsphotos-ui run build` succeeds. [FR-023, D-13] After the default test runner is known, extend `.claude/hooks/pre_commit_gate.py` to run the UI unit tests non-interactively (single run, headless) when `src/marsphotos-ui/package.json` exists. [Constitution XII]
- [ ] T059 [P] [US6] Create TypeScript models `src/marsphotos-ui/src/app/models/run-summary.ts` mirroring OpenAPI `RunSummary`/`DateResult`/`ErrorInfo`/`PhotoOutcome` (status string unions), and `src/marsphotos-ui/src/app/services/runs.service.ts` (`HttpClient`: `run()`, `latest()`, `cancel()`; base URL from `environment`; no secrets). Test `runs.service.spec.ts` with `HttpTestingController`: success, 400/500 problem mapping, 404 latest. [FR-023, contracts §openapi]
- [ ] T060 [P] [US6] Write component tests first `src/marsphotos-ui/src/app/results/results.component.spec.ts`: renders one card per date with status text, source, counts, errors (with codes); success vs failure distinguishable by text and icon not colour alone; empty state for `NoPhotos`; outside-range message; partial failure; loading state with `aria-live="polite"` announcement; error state with retry; thumbnails use `loading="lazy"` and meaningful `alt` (date + camera/id); keyboard focus order and visible focus (class present); cancel button works. [FR-023, FR-041, US6 scenarios 1-4, SC-006]
- [ ] T061 [US6] Implement `src/marsphotos-ui/src/app/app.component.*` and `results/results.component.*` (signals for state; Run and Cancel buttons; summary totals; per-date cards; semantic markup with headings/lists, `aria-live` status region, visible `:focus-visible` styles, colour contrast ≥ 4.5:1, responsive layout). Makes T060 pass. [FR-023, FR-041, FR-042]
- [ ] T062 [P] [US6] Add automated accessibility test `src/marsphotos-ui/src/app/a11y.spec.ts` using `axe-core` (dev dependency) against rendered success/empty/error states: zero serious/critical violations. [FR-041, NFR-11]
- [ ] T063 [US6] Manual UI verification recorded in `specs/001-mars-photo-downloader/verification.md`: run API+UI, click Run, observe thumbnails, tab through all controls, verify with a screen reader or Narrator that status is announced; save notes (no screenshots containing keys). [FR-041, AC-12]

**COMMIT**: `feat: scaffold Angular UI` · `feat: add Angular results page with thumbnails` · `test: add accessibility tests`

---

## Phase 10: Polish and Cross-Cutting Concerns

- [ ] T064 [P] Add `Dockerfile` (multi-stage: SDK 8 build → ASP.NET 8 runtime, non-root user, `NASA__ApiKey` passed at run time, `photos/` as volume, no secrets in layers) and `.dockerignore` (excludes `.env`, `photos/`, `node_modules/`, `.git/`). Document `docker run -e NASA__ApiKey ... -v ./photos:/app/photos` in README. Test: `docker build` succeeds; `docker history` shows no key. [DEL-7, FR-028, P3 bonus, SEC-1]
- [ ] T065 [P] Write `README.md`: purpose; architecture and layers; prerequisites; configuration table incl. `NASA__ApiKey` via `.env`/user-secrets; NASA key setup at api.nasa.gov; running CLI, API, UI; running tests (`dotnet test`, `npm test`); input file; output and `photos/` layout; error handling and exit codes; the unavailable primary service and the fallback (D-1); assumptions; AI-assisted workflow (Spec Kit phases, skills, hooks); known limitations (same-name across sources, undocumented fallback feed); future improvements (REQ §20); NASA/JPL-Caltech attribution. [FR-027, FR-043, SC-009, DEL-3, AC-9]
- [ ] T066 [P] Write `AI_NOTES.md`: tools (Claude Code, Spec Kit, Microsoft .NET skills), project skills, 2-3 best prompts, **real** examples of AI-generated defects found during implementation and the fix (to be filled from actual review findings, at least one), human corrections, architectural decisions (incl. D-1 discovery from live research), refactorings. Update incrementally at each phase end. [FR-027, DEL-4, AC-9]
- [ ] T067 Create `specs/001-mars-photo-downloader/traceability.md`: matrix Requirement → Spec FR/SC → Task IDs → Test names → AC (generated from `[Trait("AC")]` tags and task refs); list any orphan tasks or untested requirements (must be none). [Constitution I, SC-010]
- [ ] T068 Run the code-reviewer agent over all of `src/` and `tests/` (checklist: `ai-code-review` skill); fix every CRITICAL/HIGH finding via tasks, log MEDIUM/LOW in `specs/001-mars-photo-downloader/review.md`. [Constitution XII, XI]
- [ ] T069 Security review: run `dotnet list package --vulnerable --include-transitive`, `npm audit --prefix src/marsphotos-ui`, `scripts/check-no-secrets.*`, confirm CORS/static-file/URL-policy tests green; record results in `specs/001-mars-photo-downloader/review.md`. [SEC-1..SEC-10, SC-007, AC-8]
- [ ] T070 [P] Performance/behaviour check: time a real run of the four sample lines (expect < 30 s) and record in `verification.md`; confirm memory-streaming (no full-image buffering) by code review note. [SC-008, NFR-5]
- [ ] T071 Final acceptance: execute `quickstart.md` steps 1-7 on a clean clone state (fresh `photos/`), tick AC-1..AC-12 with evidence in `verification.md`, confirm second run downloads 0 (AC-5) and `git log` has ≥ 8 meaningful commits (AC-10). [AC-1–AC-12, SC-001–SC-010, SC-009 (README-only build and run, >= 8 commits)]
- [ ] T072 Run `/speckit-analyze`, then after all tasks `/speckit-converge`; repeat `implement` → `converge` until convergence. [Constitution §Workflow]

**COMMIT**: `chore: add Dockerfile` · `docs: add README and AI_NOTES` · `docs: add traceability and review records`

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2** strictly; Phase 2 blocks every story.
- **US1 (Phase 3)** needs only Phase 2. **US2** needs US1 (summary of results). **US3** needs US1 (store, use case). **US4** (failure isolation, secret-free logs) needs US1. **US5** needs US1 (ports/use case); the shared resilience pipeline (T017–T018) is foundational so US1's downloader and the US5 clients can use it. **US7 (config)** needs Phase 2 and the CLI (T032). **US6 (UI)** needs US2 (JSON schema) and US5 (to show real results).
- Live results require **US1 + US5** (the primary service is down), so the MVP is US1, US2, US3, US4 and US5 together (all P1 plus the fallback). US6/US7 are the P2 increments.
- Within a story: tests (red) → implementation (green) → commit.
- Order of stories after Phase 2: US1 → US2 → US3 → US4 → US5 → US7 → US6 → Polish.

### Parallel opportunities
- Phase 1: T003, T004, T005, T006. Phase 2: T007, T008, T009, T010, T011, T014, T015, T017 are independent files.
- US1 tests T019–T022 in parallel; US5 fixtures/tests T039–T043 in parallel; API tests T054/T055 and Angular specs T059/T060/T062 in parallel with each other (different projects).
- Polish: T064, T065, T066, T070.

## Implementation Strategy

1. **MVP**: Phases 1–2, then US1 → US5 (T001–T050) delivers the full P1 behaviour with real photos from the fallback; validate with CLI and quickstart steps 1–5.
2. Add US7 (T051–T053), then US6 (T054–T063) for the UI.
3. Polish (T064–T072) and converge.
4. After every phase: build, test, code-review, fix, re-test; update `AI_NOTES.md`; commit small.

## Task Count Summary
| Phase | Tasks |
|---|---|
| 1 Setup | 6 (T001–T006) |
| 2 Foundational | 12 (T007–T018) |
| 3 US1 | 10 (T019–T028) |
| 4 US2 | 4 (T029–T032) |
| 5 US3 | 3 (T033–T035) |
| 6 US4 | 3 (T036–T038) |
| 7 US5 | 12 (T039–T050) |
| 8 US7 | 3 (T051–T053) |
| 9 US6 | 10 (T054–T063) |
| 10 Polish | 9 (T064–T072) |
| **Total** | **72** |

# Implementation Plan: Mars Rover Photo Downloader

**Branch**: `001-mars-photo-downloader` | **Date**: 2026-10-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-mars-photo-downloader/spec.md`

## Summary

Read dates from `dates.txt`, parse and validate them, look up photos for each valid in-range date through a **provider chain** (the Mars Rover Photos service first, NASA's raw-image feed as automatic fallback), download the first N photos into `photos/<yyyy-MM-dd>/` idempotently, and report results via a console summary, a JSON API and an Angular results page.

Approach: a small layered .NET 8 solution (Domain / Application / Infrastructure) with two thin hosts that share the same use case — a **console runner** and an **ASP.NET Core minimal-API host** — plus an Angular SPA. No database, queue or other infrastructure (Constitution II). All external I/O sits behind ports so the whole flow is testable with fakes and a mocked `HttpMessageHandler`.

> **Decision D-1 — APPROVED 2026-10-06 (amends REQ FR-3.13)**: live research (see `research.md` R-3) shows the original service's `earth_date` means *the Earth date assigned to a Mars day (sol)*, not the UTC capture time. Filtering fallback photos by `date_taken_utc` yields **0 photos for 2018-06-02** (sol 2069 holds 60 photos, mostly captured 2018-06-01 UTC). The plan therefore selects fallback photos by **the sol that corresponds to the requested date**. This preserves the original service's semantics and meets SC-002 for all three sample dates. Spec FR-011 and REQ FR-3.13 carry the approved amendment.

## Technical Context

**Language/Version**: C# 12 on .NET 8 (`net8.0`), pinned by `global.json` (SDK 8.0.204, `rollForward: latestFeature`; the machine also has SDKs 9/10). Angular CLI 22.x (current), TypeScript strict, standalone components.

**Primary Dependencies**: ASP.NET Core minimal APIs; `Microsoft.Extensions.Http.Resilience` (retry, timeout; Polly-based); `Microsoft.Extensions.Options` + `ValidateOnStart`; `Microsoft.Extensions.Logging` (console, structured). No third-party parsing, mapping or DI libraries.

**Storage**: Local file system only (`photos/<yyyy-MM-dd>/`, temp `.part` then atomic move). Last run summary held in memory by the API host.

**Testing**: xUnit; `HttpMessageHandler` fakes for provider and download tests; `WebApplicationFactory<Program>` for API integration with stubbed providers; temp-directory file system tests; Angular default test runner for the SPA. Recorded JSON fixtures of both providers (including the HTML "No such app" page). No live NASA calls in default test runs; one opt-in live smoke test.

**Target Platform**: Windows/Linux/macOS developer machine; optional Docker image (bonus, P3).

**Project Type**: web application (API + SPA) with a console runner.

**Performance Goals**: 4 dates × 5 photos in < 30 s with healthy sources (SC-008); streamed downloads, O(1) memory per image.

**Constraints**: provider limit ~2000 requests/h with the project key; sequential date lookups, ≤3 concurrent downloads per date; 30 s lookup timeout, 60 s download timeout; 25 MB per-image cap.

**Scale/Scope**: tens of dates, ≤5 photos each; single operator; no multi-user, no auth.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see bottom of this table.*

| Principle | Plan compliance |
|---|---|
| I Spec-first & traceability | Spec, plan, tasks precede code; traceability matrix in `traceability.md` (created in tasks phase); hook blocks `src/` edits until artifacts exist. ✅ |
| II Simplicity | 4 `src` .NET projects + CLI + SPA, no DB/queue/cache/microservices. Provider fallback is a real requirement (OQ-10). ✅ |
| III Clean architecture / SOLID / DI | Domain ← Application (ports) ← Infrastructure; hosts compose. Constructor DI only. ✅ |
| IV Secure configuration | `NASA__ApiKey` via env / gitignored `.env` / user-secrets, `IOptions` + `ValidateOnStart`; key redacted in logs; never in Angular. ✅ |
| V Input validation | `TryParseExact` + invariant culture; URL host allow-list; sanitized file stems; size cap. ✅ |
| VI Async | Async I/O end-to-end, `CancellationToken` everywhere, `IHttpClientFactory`, streamed downloads. ✅ |
| VII Resilience | Retry/backoff+jitter for 429/5xx/timeout/network, honor `Retry-After`; no retry on 4xx; per-item isolation. ✅ |
| VIII Idempotency | Deterministic filenames, skip non-empty existing, `.part` + atomic move. ✅ |
| IX Errors & logging | Stable error-code catalogue; result types; structured logs; global handler. ✅ |
| X Testing | Test matrix in quickstart/tasks; tests-first for parser, sol conversion, store. ✅ |
| XI AI code verification | `ai-code-review` skill + reviewer agent per phase; AI_NOTES.md. ✅ |
| XII Quality gates | Nullable on, warnings-as-errors, `.editorconfig`; pre-commit hook runs build+test. ✅ |
| XIII–XV | README/AI_NOTES; small commits; accessible UI with lazy thumbnails. ✅ |

**Post-design re-check**: ✅ no violations. Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-mars-photo-downloader/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── openapi.yaml             # HTTP API exposed by the host
│   ├── photo-provider.md        # Port contract + the two external provider contracts
│   └── cli.md                   # Console runner contract (args, output, exit codes)
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
global.json
MarsPhotos.sln
Directory.Build.props            # net8.0, Nullable, TreatWarningsAsErrors, analyzers
.editorconfig
dates.txt                        # exactly the four sample lines
.env.example                     # placeholder only; real .env is gitignored
src/
├── MarsPhotos.Domain/           # records, enums, error codes, mission ranges (no dependencies)
├── MarsPhotos.Application/      # DateLineParser, ProcessDatesUseCase, ProviderChain, ports
├── MarsPhotos.Infrastructure/   # NASA clients, SolConverter, HttpPhotoDownloader,
│                                #   FileSystemPhotoStore, UrlPolicy, options, .env loader
├── MarsPhotos.Api/              # minimal-API host: /api/runs, /health, static /photos, CORS
├── MarsPhotos.Cli/              # console runner host: summary table + exit codes
└── marsphotos-ui/               # Angular SPA (standalone, strict)
tests/
├── MarsPhotos.Tests/            # unit tests (parser, sol, providers, store, orchestrator)
└── MarsPhotos.Api.Tests/        # WebApplicationFactory integration tests
Dockerfile                       # P3 / bonus
README.md
AI_NOTES.md
```

**Structure Decision**: Layered .NET solution with two thin hosts (Api, Cli) rather than one project, because the brief allows console OR API and both must report the same results from one use case. Two test projects separate fast unit tests from host integration tests. The Angular app lives under `src/` so the repository has a single source root. No separate backend/frontend repos or folders beyond this.

### Key Design Decisions (summary; rationale in research.md)

| # | Decision | Traces to |
|---|---|---|
| D-1 | Fallback selects photos by **sol derived from the date** (amendment approved) | FR-010, FR-011, SC-002, F-4 |
| D-2 | `IPhotoProvider` port; `ProviderChain` tries primary then fallback; result records which provider served | FR-010, OQ-10 |
| D-3 | Primary provider treats 404, HTML/non-JSON, 5xx-after-retries as `Unavailable` → fallback; JSON `{"photos":[]}` is `NoPhotos`, **no** fallback | FR-009, FR-012 |
| D-4 | Parsing: `DateOnly.TryParseExact` with a documented format array, invariant culture, two-digit-year → 2000–2099 | FR-003–FR-005 |
| D-5 | Out-of-range dates (before landing or after today UTC) skip all lookups; reported `OutsideMissionRange` | FR-007 |
| D-6 | Select first N after **sorting by stable photo id (ordinal)** for determinism; fallback prefers `full_res`, else `large` | FR-008 |
| D-7 | File name = sanitized provider stem + extension from an allow-list (`.jpg/.jpeg/.png`); write `.part` then `File.Move` | FR-014–FR-016 |
| D-8 | Resilience via `Microsoft.Extensions.Http.Resilience` with custom retry predicate (no 4xx retry, honor `Retry-After`) | FR-012, FR-013 |
| D-9 | `.env` loaded by a ~20-line dev-only loader in Infrastructure (no extra package); user-secrets also supported | FR-024, SEC-10 |
| D-10 | Dates processed sequentially; ≤3 concurrent downloads per date | FR-013, NFR-6 |
| D-11 | API host keeps the last `RunSummary` in memory; photos served as static files from the photos root only | FR-020, FR-023 |
| D-12 | Exit code `0` when the run completes (even with per-date problems), `1` fatal config/IO, `2` unexpected crash | FR-022, OQ-7 |
| D-13 | Angular dev server proxies `/api` and `/photos` to the API; CORS allows only `http://localhost:4200` | FR-023, SEC-7 |

## Complexity Tracking

> No constitution violations. Table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |

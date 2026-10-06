# Software Requirements Specification — Mars Rover Photo Downloader

| Field | Value |
|---|---|
| Document ID | SRS-MRPD-001 |
| Version | 1.0 (Draft for review) |
| Date | 2026-10-06 |
| Source | `Requirement.docx` (assignment brief) |
| Status | Baseline candidate |
| Audience | Developers, reviewers/evaluators, QA, DevOps |

---

## Table of Contents
1. Introduction
2. Business Context & Objectives
3. Stakeholders
4. Scope
5. Assumptions, Constraints, Dependencies
6. System Context & Architecture Overview
7. Functional Requirements
8. External Interface Requirements
9. Data Requirements
10. Non-Functional Requirements
11. Error Handling & Logging Requirements
12. Security Requirements
13. Deliverables & Documentation Requirements
14. Acceptance Criteria
15. Testing Requirements
16. Risks & Mitigations
17. Open Questions & Clarifications
18. Traceability Matrix
19. Code Walkthrough Readiness
20. Future Enhancements (Production Roadmap)
21. Glossary

---

## 1. Introduction

### 1.1 Purpose
This document specifies the business, functional, non-functional, and delivery requirements for a small application that reads calendar dates from a text file, validates them, retrieves matching photographs from the **NASA Mars Rover Photos API**, stores them locally without duplication, and reports a clear summary of results.

### 1.2 Nature of the Work
The assignment is a time-boxed engineering exercise. It evaluates (a) correctness and robustness of the solution, (b) architecture and code quality, and (c) **AI-assisted development skill** (how AI output was directed, verified, and corrected). Requirements are therefore written to be testable and to make design decisions explicit and defensible in a code walkthrough.

### 1.3 Requirement Conventions
- **MUST / SHALL** — mandatory (assignment-required or necessary for correctness).
- **SHOULD** — strongly recommended; omission needs justification.
- **MAY** — optional / bonus.
- IDs: `BR` business, `FR` functional, `NFR` non-functional, `SEC` security, `DEL` deliverable, `AC` acceptance criterion.
- Priority: **P1** = required by the brief, **P2** = needed for quality, **P3** = bonus.

---

## 2. Business Context & Objectives

### 2.1 Problem Statement
Users hold a list of dates written in inconsistent, human-entered formats (some invalid). They want the Mars rover imagery taken on those Earth dates retrieved and archived locally, with transparent reporting of what succeeded and what failed.

### 2.2 Business Objectives
| ID | Objective | Measure of success |
|---|---|---|
| BR-1 | Demonstrate reliable ingestion of messy, multi-format date input | 100% of valid sample dates parsed; 0 crashes on invalid input |
| BR-2 | Demonstrate robust integration with a third-party REST API | Correct handling of success, empty, rate-limited, and failed responses |
| BR-3 | Demonstrate efficient, idempotent local storage | Re-running the app performs zero repeat downloads |
| BR-4 | Provide transparent, user-comprehensible output | Per-date summary with date, count, and errors |
| BR-5 | Demonstrate disciplined, auditable AI-assisted development | `AI_NOTES.md` complete; visible incremental commit history |
| BR-6 | Demonstrate production-minded engineering | Clean architecture, tests, logging, containerization (bonus) |

### 2.3 Value Proposition
A repeatable, safe-to-rerun pipeline: **file → parse/validate → fetch → download → store → report**.

---

## 3. Stakeholders

| Stakeholder | Interest |
|---|---|
| Candidate / Developer | Delivers and defends the solution |
| Reviewer / Evaluator | Assesses correctness, quality, AI usage, walkthrough |
| End User (operator) | Runs the tool, reads the summary |
| NASA API (external system) | Data provider; imposes rate limits and terms of use |

---

## 4. Scope

### 4.1 In Scope
1. Reading `dates.txt` and parsing multiple date formats.
2. Detecting and reporting invalid dates without crashing.
3. Calling the NASA Mars Rover Photos API by `earth_date`.
4. Downloading the first 3–5 photos per valid date.
5. Storing images under `photos/<yyyy-MM-dd>/` with duplicate avoidance.
6. Reporting results (console and/or JSON API endpoint) — plus an Angular UI (see 4.3).
7. Documentation: `README.md`, `AI_NOTES.md`.
8. Incremental git history.

### 4.2 Out of Scope
- Image processing, analysis, or transformation.
- User authentication/authorization and multi-user features.
- Database persistence (file-system storage only).
- Cloud deployment, CI/CD pipeline provisioning (optional Docker only).
- Support for other NASA APIs.

### 4.3 Scope Clarification — Angular UI
The brief's output section lists UI as "optional" and bonus lists "Simple UI that shows thumbnails", **but Tech Requirements state "Use Angular for UI"**. This document treats Angular as the **mandated technology for any UI delivered**, and recommends delivering a minimal Angular UI (P2) alongside the API (see Open Question OQ-1). The CLI/console output remains the guaranteed minimum.

---

## 5. Assumptions, Constraints, Dependencies

### 5.1 Assumptions
| ID | Assumption | Rationale |
|---|---|---|
| A-1 | Two-digit year `17` in `02/27/17` is interpreted as **2017** | Only plausible reading; matches `photos/2017-02-27/` example in brief |
| A-2 | Slash-separated numeric dates are **US order (MM/dd/yy)** | `02/27/17` is unambiguous (27 cannot be a month); US convention adopted and documented |
| A-3 | Default rover is **Curiosity** | Only rover active on all sample dates (Spirit ended 2010; Opportunity ended June 2018 and its photo coverage on some dates may be absent). Configurable. |
| A-4 | "First 3–5 photos" = the first N items returned by the API, where N is configurable (default **5**, minimum 3) | Brief allows a range |
| A-5 | Each line of `dates.txt` contains exactly one date; blank lines are ignored | Simplest consistent contract |
| A-6 | Interpreted dates are Earth dates (not Martian sols) | API parameter `earth_date` |
| A-7 | Dates with no photos are a **valid, non-error outcome** reported as "0 photos / no photos available" | API legitimately returns empty arrays |
| A-8 | Execution has outbound internet access to `api.nasa.gov` and the image CDN | Required by design |

### 5.2 Constraints
| ID | Constraint |
|---|---|
| C-1 | Backend MUST be C# on **.NET 8** (or .NET Core). |
| C-2 | UI, if provided, MUST use **Angular**. |
| C-3 | `async/await` MUST be used appropriately for all I/O. |
| C-4 | API keys MUST NOT be hardcoded or committed. |
| C-5 | Solution MUST build and run locally. |
| C-6 | Must respect NASA API rate limits (see 5.3). |

### 5.3 External Dependencies
| Dependency | Detail | Impact |
|---|---|---|
| NASA Open APIs (`https://api.nasa.gov`) | Endpoint `GET /mars-photos/api/v1/rovers/{rover}/photos?earth_date=YYYY-MM-DD&api_key=…` | Core data source |
| API key | Free key from api.nasa.gov; `DEMO_KEY` is available but heavily limited (~30 requests/hour/IP, ~50/day/IP at time of writing — verify) | Rate-limit risk during testing |
| Image host | Photo `img_src` URLs (NASA/JPL hosts; may be HTTP or redirect) | Download failures, redirects, mixed content |
| Docker (optional) | Containerized execution | Bonus |

---

## 6. System Context & Architecture Overview

### 6.1 Logical Flow
```
dates.txt ─► DateFileReader ─► DateParser/Validator ─┬─► invalid ─► Error list
                                                     │
                                                     └─► valid ─► NasaPhotosClient ─► PhotoDownloader ─► photos/<date>/
                                                                                                │
                                                                          ResultAggregator ◄────┘
                                                                                │
                                                          Console summary / JSON API / Angular UI
```

### 6.2 Recommended Architecture (Clean Architecture, pragmatic)
| Layer | Responsibility | Example components |
|---|---|---|
| Domain | Entities/value objects, no dependencies | `ParsedDate`, `RoverPhoto`, `DateResult`, `RunSummary` |
| Application | Use-case orchestration, abstractions | `ProcessDatesUseCase`, `IDateParser`, `IPhotoProvider`, `IPhotoStore` |
| Infrastructure | External concerns | `NasaMarsPhotosClient` (typed `HttpClient`), `FileSystemPhotoStore`, `FileDateSource` |
| Presentation | Entry points | Console host and/or ASP.NET Core Web API; Angular SPA |

Design principles: dependency inversion, single responsibility, constructor DI, `IHttpClientFactory`, `IOptions<T>` configuration, `CancellationToken` propagation.

### 6.2.1 Suggested Repository Structure
```
/src
  /MarsPhotos.Domain
  /MarsPhotos.Application
  /MarsPhotos.Infrastructure
  /MarsPhotos.Api            (Web API + console runner entry)
  /marsphotos-ui             (Angular)
/tests
  /MarsPhotos.Tests
dates.txt
README.md
AI_NOTES.md
Dockerfile
.gitignore
```

---

## 7. Functional Requirements

### 7.1 FR-1 — Date Input from File (P1)

| ID | Requirement |
|---|---|
| FR-1.1 | The system SHALL provide a `dates.txt` file containing exactly these lines: `02/27/17`, `June 2, 2018`, `Jul-13-2016`, `April 31, 2018`. |
| FR-1.2 | The system SHALL read the file path from configuration/CLI argument, defaulting to `dates.txt`. |
| FR-1.3 | The system SHALL read the file asynchronously, line by line, tolerating UTF-8 with/without BOM and both `\n` and `\r\n` endings. |
| FR-1.4 | The system SHALL trim whitespace (including trailing spaces present in the source sample) and skip blank lines. |
| FR-1.5 | If the file is missing, empty, or unreadable, the system SHALL report a clear error and exit with a non-zero code without an unhandled exception. |
| FR-1.6 | Lines beginning with `#` MAY be treated as comments (optional). |

### 7.2 FR-2 — Date Parsing & Validation (P1)

| ID | Requirement |
|---|---|
| FR-2.1 | The parser SHALL support at minimum these formats: `MM/dd/yy`, `MMMM d, yyyy`, `MMM-dd-yyyy`. |
| FR-2.2 | The parser SHOULD additionally accept reasonable variants: `MM/dd/yyyy`, `yyyy-MM-dd` (ISO 8601), `MMM d, yyyy`, `d MMMM yyyy`, `MMMM-d-yyyy`, case-insensitive month names. Supported formats MUST be an explicit, documented list. |
| FR-2.3 | Parsing SHALL use `DateTime.TryParseExact` (or `DateOnly.TryParseExact`) with an explicit format array and `CultureInfo.InvariantCulture` — **not** culture-dependent free-form `DateTime.Parse`. |
| FR-2.4 | Two-digit years SHALL be resolved deterministically and documented (e.g., `yy` → 2000–2099, or use a pivot) so that `17` ⇒ 2017 independent of machine culture settings. |
| FR-2.5 | Calendar-invalid dates (e.g., `April 31, 2018`) SHALL be classified as **Invalid** with reason such as "Day 31 does not exist in April 2018", never as exceptions. |
| FR-2.6 | Unparseable text SHALL be classified as **Unrecognized format** with the original text preserved. |
| FR-2.7 | The system SHALL normalize valid dates to ISO `yyyy-MM-dd` for API calls and folder names. |
| FR-2.8 | Duplicate dates (same normalized value from different lines) SHALL be processed once and the duplication noted in the output. |
| FR-2.9 | Dates in the future, or earlier than the rover's landing date, SHALL NOT trigger a photo-source call and SHALL be reported as "outside rover mission range" (0 photos, no error); all in-range valid dates SHALL be queried. *(Clarified 2026-10-06)* |
| FR-2.10 | One bad line SHALL NEVER prevent processing of the remaining lines. |

**Expected parse outcomes for the provided sample**

| Raw input | Expected result | Normalized | Folder |
|---|---|---|---|
| `02/27/17` | Valid | 2017-02-27 | `photos/2017-02-27/` |
| `June 2, 2018` | Valid | 2018-06-02 | `photos/2018-06-02/` |
| `Jul-13-2016` | Valid | 2016-07-13 | `photos/2016-07-13/` |
| `April 31, 2018` | **Invalid** (April has 30 days) | — | none |

### 7.3 FR-3 — NASA Mars Rover Photos API Integration (P1)

| ID | Requirement |
|---|---|
| FR-3.1 | For each valid date the system SHALL call `GET /mars-photos/api/v1/rovers/{rover}/photos` with `earth_date` and `api_key`. |
| FR-3.2 | The rover SHALL be configurable (`curiosity` default; `opportunity`, `spirit` permitted). |
| FR-3.3 | The system SHALL select the first N photos (default 5; configurable 3–5) from the response in API order, deterministically. |
| FR-3.4 | The system SHALL use a typed `HttpClient` via `IHttpClientFactory` with a base address, timeout, and default headers from configuration. |
| FR-3.5 | The response SHALL be deserialized into DTOs, then mapped to domain `RoverPhoto` (id, sol, camera, img_src, earth_date, rover). Missing/null fields MUST NOT cause crashes. |
| FR-3.6 | An empty `photos` array SHALL be reported as "No photos found for rover X on date Y" (not a failure). |
| FR-3.7 | HTTP errors SHALL be mapped to categorized outcomes: 400/404 (bad request/not found), 403 (invalid key), 429 (rate limit), 5xx (server), timeouts, DNS/connection failures, malformed JSON. |
| FR-3.8 | **Transient** failures (429, 5xx, timeouts, network errors) SHOULD be retried with exponential backoff + jitter (e.g., 3 attempts), honoring `Retry-After` when present (Polly or equivalent). **Permanent** failures (400, 403, 404) SHALL NOT be retried. |
| FR-3.9 | The system SHOULD inspect `X-RateLimit-Remaining` response headers and log a warning when low. |
| FR-3.10 | Dates MAY be processed with bounded concurrency (e.g., `SemaphoreSlim`, max 2–3) or sequentially; the chosen approach SHALL respect rate limits and be configurable. |

### 7.4 FR-4 — Image Download & Local Storage (P1)

| ID | Requirement |
|---|---|
| FR-4.1 | Photos SHALL be stored under `photos/<yyyy-MM-dd>/` relative to a configurable root directory. |
| FR-4.2 | File names SHALL be deterministic and unique, e.g. `<photoId>_<camera>.jpg` (extension derived from the URL, defaulting to `.jpg`). File names MUST be sanitized for Windows/Linux. |
| FR-4.3 | **Idempotency:** if the target file already exists and is non-empty, the system SHALL skip the download and count it as "skipped (already exists)". |
| FR-4.4 | Downloads SHALL stream to a **temporary file** (`.part`) and be atomically renamed on success, so interrupted downloads never leave a corrupt file that is mistaken for complete. Stale `.part` files SHALL be overwritten/cleaned. |
| FR-4.5 | Downloads SHALL be asynchronous, use streams (no full-image buffering in memory), and honor `CancellationToken`. |
| FR-4.6 | A failure to download one image SHALL NOT abort other images or dates; it is recorded as a per-photo error. |
| FR-4.7 | Download requests SHALL use timeouts and retry transient failures per FR-3.8; redirects (including HTTP→HTTPS) SHALL be followed. |
| FR-4.8 | The system SHOULD validate the response (HTTP 200, non-zero length, `image/*` content type) before committing the file. |
| FR-4.9 | Directories SHALL be created on demand; directory creation failures (permissions, disk full) SHALL be reported clearly. |
| FR-4.10 | The system SHALL NOT overwrite existing valid files and SHALL NOT delete user data. |

### 7.5 FR-5 — Output & Reporting (P1)

| ID | Requirement |
|---|---|
| FR-5.1 | The system SHALL present, per input line, at minimum: **the date**, **number of photos downloaded**, and **any validation or API errors**. |
| FR-5.2 | Console output SHALL be a clear, aligned summary (table or structured lines) and a final totals section. |
| FR-5.3 | The system SHALL distinguish counts of: *downloaded*, *skipped (already existed)*, *failed*, and *photos available/selected*. |
| FR-5.4 | The system SHOULD expose the same result as JSON via an HTTP endpoint (e.g., `POST /api/runs` or `GET /api/results`) with a documented schema (see §8.3). |
| FR-5.5 | The Angular UI (if delivered) SHALL show a per-date result list/cards, errors, and **thumbnails** of stored images served by the API. |
| FR-5.6 | The process exit code SHALL reflect status: `0` all valid dates processed; non-zero for fatal errors. A policy for partial failures (e.g., exit `0` with warnings vs. `2`) SHALL be documented. |
| FR-5.7 | Output SHALL NOT include the API key. |

**Reference console output (illustrative)**
```
Mars Rover Photo Downloader — rover: curiosity
------------------------------------------------------------------------
Input               Date        Status    Downloaded  Skipped  Errors
02/27/17            2017-02-27  OK        5           0        -
June 2, 2018        2018-06-02  OK        5           0        -
Jul-13-2016         2016-07-13  OK        3           2        -
April 31, 2018      -           INVALID   0           0        Day 31 does not exist in April 2018
------------------------------------------------------------------------
Totals: 4 lines | 3 valid | 1 invalid | 13 downloaded | 2 skipped | 0 failed
```

### 7.6 FR-6 — Configuration (P1)

| ID | Requirement |
|---|---|
| FR-6.1 | All settings SHALL be externalized via `appsettings.json`, environment variables, and CLI args (precedence: CLI > env > file). |
| FR-6.2 | Configurable items: API key, base URL, rover, photos per date (3–5), input file path, output root, timeouts, retry count, max concurrency. |
| FR-6.3 | The API key SHALL be supplied via environment variable (e.g., `NASA__ApiKey` / `NASA_API_KEY`), user-secrets, or secret store — never committed. |
| FR-6.4 | Configuration SHALL be validated at startup (`ValidateOnStart`); missing key SHALL produce an actionable message. A documented fallback to `DEMO_KEY` MAY be allowed with a warning about its limits. |

### 7.7 FR-7 — Angular UI (P2 / mandated if UI delivered)

| ID | Requirement |
|---|---|
| FR-7.1 | Angular (current LTS) with TypeScript strict mode; standalone components preferred. |
| FR-7.2 | UI SHALL allow triggering a run (using server-side `dates.txt`, or optionally pasted/uploaded dates) and display the result summary. |
| FR-7.3 | UI SHALL show validation errors and API errors per date with clear styling and status badges. |
| FR-7.4 | UI SHOULD show image thumbnails per date (lazy-loaded) from an API static-file endpoint. |
| FR-7.5 | UI SHALL show loading, empty, and error states; use an Angular service + `HttpClient`; API base URL via environment config; CORS configured for local dev. |
| FR-7.6 | UI SHOULD be accessible (semantic markup, alt text, keyboard navigation) and responsive. |

---

## 8. External Interface Requirements

### 8.1 NASA Mars Rover Photos API (consumed)
- **Request:** `GET https://api.nasa.gov/mars-photos/api/v1/rovers/curiosity/photos?earth_date=2017-02-27&api_key={KEY}`
- **Response (relevant fields):**
```json
{
  "photos": [
    {
      "id": 102693,
      "sol": 1621,
      "camera": { "id": 20, "name": "FHAZ", "full_name": "Front Hazard Avoidance Camera" },
      "img_src": "http://mars.jpl.nasa.gov/.../image.JPG",
      "earth_date": "2017-02-27",
      "rover": { "id": 5, "name": "Curiosity", "status": "active" }
    }
  ]
}
```
- The exact schema SHALL be verified against a live response when implementing; DTOs MUST tolerate additional/unknown fields.

### 8.2 Local File System
- Input: `dates.txt`. Output: `photos/<yyyy-MM-dd>/<file>`. Logs: console (optionally `logs/` rolling file).

### 8.3 Result API (produced, recommended)
| Method | Path | Description |
|---|---|---|
| `POST` | `/api/runs` | Execute processing (body optional: list of dates / rover override); returns `RunSummary` |
| `GET` | `/api/runs/latest` | Last run's summary |
| `GET` | `/photos/{date}/{file}` | Static serving of stored images (for thumbnails) |
| `GET` | `/health` | Liveness check |

**`RunSummary` JSON (illustrative)**
```json
{
  "rover": "curiosity",
  "startedAt": "2026-10-06T10:00:00Z",
  "durationMs": 8421,
  "totals": { "lines": 4, "valid": 3, "invalid": 1, "downloaded": 13, "skipped": 2, "failed": 0 },
  "results": [
    {
      "input": "02/27/17",
      "date": "2017-02-27",
      "status": "Success",
      "photosAvailable": 20,
      "downloaded": 5,
      "skipped": 0,
      "failed": 0,
      "files": ["photos/2017-02-27/102693_FHAZ.jpg"],
      "errors": []
    },
    {
      "input": "April 31, 2018",
      "date": null,
      "status": "InvalidDate",
      "downloaded": 0,
      "errors": [ { "code": "DATE_INVALID", "message": "Day 31 does not exist in April 2018" } ]
    }
  ]
}
```
Errors SHOULD follow RFC 7807 `ProblemDetails` for HTTP-level failures.

---

## 9. Data Requirements

### 9.1 Domain Model
| Entity | Key attributes |
|---|---|
| `DateInputLine` | LineNumber, RawText |
| `ParsedDate` | RawText, IsValid, `DateOnly? Value`, ErrorCode, ErrorMessage |
| `RoverPhoto` | Id, Sol, CameraName, ImageUrl, EarthDate, RoverName |
| `PhotoDownloadOutcome` | PhotoId, Status (Downloaded/Skipped/Failed), FilePath, Error |
| `DateResult` | Input, Date, Status, PhotosAvailable, Outcomes[], Errors[] |
| `RunSummary` | Rover, StartedAt, Duration, Totals, Results[] |

### 9.2 Status & Error Code Catalogue
| Code | Meaning |
|---|---|
| `DATE_UNRECOGNIZED` | Text matches no supported format |
| `DATE_INVALID` | Matches a format but is not a real calendar date |
| `DATE_OUT_OF_RANGE` | Before landing / in future (warning) |
| `DATE_DUPLICATE` | Same date appeared earlier |
| `API_NO_PHOTOS` | Valid response with zero photos |
| `API_RATE_LIMITED` | HTTP 429 after retries |
| `API_AUTH` | HTTP 403 / invalid key |
| `API_UNAVAILABLE` | 5xx / timeout / network |
| `API_BAD_RESPONSE` | Malformed JSON/unexpected schema |
| `DL_FAILED` | Image download failed after retries |
| `IO_ERROR` | File-system permission/disk error |

### 9.3 Storage Rules
- Storage root and per-date directories are derived solely from normalized dates (no user-controlled path segments → prevents traversal).
- File names derive from numeric photo ID + sanitized camera name.

---

## 10. Non-Functional Requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-1 | Correctness | Date parsing is deterministic and independent of OS/culture settings. |
| NFR-2 | Reliability | No unhandled exceptions for any input file content; one failure is isolated from others (fault isolation). |
| NFR-3 | Idempotency | Re-running with identical inputs produces no duplicate downloads and the same on-disk state. |
| NFR-4 | Resilience | Retry with backoff for transient faults; bounded timeouts (e.g., 30 s API, 60 s image); graceful cancellation (Ctrl+C). |
| NFR-5 | Performance | Typical run (4 dates × 5 images) SHOULD complete in < 30 s on a normal connection; images streamed, memory use O(1) per download. |
| NFR-6 | Scalability | Design supports hundreds of dates via bounded concurrency; not dependent on loading all images in memory. |
| NFR-7 | Maintainability | SOLID, layered architecture; methods small and cohesive; no static state; meaningful names; XML docs on public abstractions. |
| NFR-8 | Testability | All external I/O behind interfaces; time/file-system/HTTP mockable. |
| NFR-9 | Observability | Structured logging (`ILogger`, optionally Serilog) with levels and correlation per date; no secrets in logs. |
| NFR-10 | Portability | Runs on Windows, Linux, macOS; path handling via `Path.Combine`; Dockerfile (bonus). |
| NFR-11 | Usability | Output is readable without documentation; errors are actionable (state cause + remedy). |
| NFR-12 | Compliance | Respect NASA API terms and rate limits; attribute NASA imagery appropriately in README. |
| NFR-13 | Code style | `.editorconfig`, nullable reference types enabled, warnings-as-errors for new code (recommended). |
| NFR-14 | Async | Use `async/await` end-to-end, no `.Result`/`.Wait()`, `ConfigureAwait` per context, `CancellationToken` everywhere; `Task.WhenAll` only with bounded concurrency. |

---

## 11. Error Handling & Logging Requirements

### 11.1 Error-Handling Strategy
| Layer | Strategy |
|---|---|
| Parsing | `TryParse` pattern returning a result object; **no exceptions for control flow** |
| API client | Typed results/exception translation at the boundary; categorized by FR-3.7 |
| Resilience | Retry/backoff on transient; fail fast on permanent |
| Download | Per-photo isolation; temp-file + atomic move; cleanup on failure |
| Orchestrator | Aggregates all outcomes; never aborts the batch for per-item errors |
| Host | Global handler for truly unexpected errors → logs + non-zero exit |

### 11.2 Logging
- Levels: `Information` (progress), `Warning` (skips, low rate limit), `Error` (failures), `Debug` (HTTP detail).
- Each log entry SHOULD include: date, rover, photo id, attempt number.
- API key MUST be redacted from URLs in logs (important: the key is a query parameter — disable default HttpClient URL logging or redact).

---

## 12. Security Requirements

| ID | Requirement |
|---|---|
| SEC-1 | No API key in source, config committed to VCS, README examples, logs, or JSON output. `.gitignore` SHALL exclude secrets/local config (e.g., `appsettings.*.local.json`, `.env`, `photos/`). |
| SEC-2 | Provide `.env.example` / documented env var name with placeholder value only. |
| SEC-3 | Prevent path traversal: file/folder names are generated from validated values; sanitize any API-derived string used in paths. |
| SEC-4 | Only download from expected hosts (allow-list e.g. `*.nasa.gov`, `*.jpl.nasa.gov`) and only `http/https` schemes (SSRF/untrusted URL mitigation). Cap max download size (e.g., 25 MB). |
| SEC-5 | Prefer HTTPS; upgrade `http://` image URLs to `https://` where supported. |
| SEC-6 | Never `eval`/execute downloaded content; validate content type. |
| SEC-7 | Web API/Angular: restrictive CORS (dev origin only), no sensitive data exposure, static file serving limited to the photos directory. |
| SEC-8 | Dependencies pinned and free of known vulnerabilities (`dotnet list package --vulnerable`, `npm audit`). |
| SEC-9 | Input file size/line length limits to avoid resource exhaustion. |

---

## 13. Deliverables & Documentation Requirements

| ID | Deliverable | Content requirements |
|---|---|---|
| DEL-1 | Source code | .NET 8 solution (+ Angular app if delivered), builds with `dotnet build` |
| DEL-2 | `dates.txt` | Exactly the four specified lines |
| DEL-3 | `README.md` | Prerequisites (.NET 8 SDK, Node/Angular CLI), how to obtain & set the API key, how to build/run (CLI, API, UI, Docker), configuration table, sample output, how to run tests, **assumptions** (see §5.1), known limitations |
| DEL-4 | `AI_NOTES.md` | (1) AI tool(s) used; (2) 2–3 best prompts; (3) one concrete example of incorrect/incomplete AI output and the fix; (4) significant post-generation changes |
| DEL-5 | Git history | Multiple small, meaningful commits (see 13.1) |
| DEL-6 | Tests (bonus) | Unit tests for parsing and API mapping |
| DEL-7 | `Dockerfile` (bonus) | Multi-stage build, non-root user, documented `docker run` with env var key and volume mount for `photos/` |

### 13.1 Commit Plan (suggested, illustrating incremental delivery)
1. `chore: initialize solution, .gitignore, editorconfig`
2. `feat: add dates.txt and file reader`
3. `feat: add multi-format date parser with validation`
4. `test: add date parsing unit tests`
5. `feat: add NASA Mars Photos typed client and DTO mapping`
6. `feat: add resilient HTTP policy (retry/backoff, timeouts)`
7. `feat: add image downloader with idempotent storage`
8. `feat: add orchestrator and console summary output`
9. `feat: add Web API endpoints and static photo serving`
10. `feat: add Angular UI with thumbnails`
11. `chore: add Dockerfile`
12. `docs: add README and AI_NOTES`

---

## 14. Acceptance Criteria

Given/When/Then format for testability.

| ID | Criterion | Verification |
|---|---|---|
| AC-1 | **Build/run:** On a clean machine with .NET 8 SDK and an API key, `dotnet build` succeeds with no errors and the documented run command starts successfully. | Manual / CI |
| AC-2 | **Parsing:** Given the four sample lines, then exactly three are valid (2017-02-27, 2018-06-02, 2016-07-13) and `April 31, 2018` is reported invalid with a clear reason; the app does not crash. | Unit + manual |
| AC-3 | **API call:** For each valid date the API is called using `earth_date`; at least 3 photos (up to 5) are selected when available. | Integration/log |
| AC-4 | **Storage:** Images exist under `photos/2017-02-27/`, `photos/2018-06-02/`, `photos/2016-07-13/`; no folder is created for the invalid date. | Manual |
| AC-5 | **Idempotency:** Given a completed run, when re-run, then zero files are re-downloaded (all reported as skipped) and file timestamps are unchanged. | Manual / test |
| AC-6 | **Network failure:** Given network disabled or API returning 5xx/429, then the app retries, then reports a clear error per affected date, exits cleanly, and leaves no corrupt/partial image files. | Fault injection |
| AC-7 | **Output:** Summary lists per date: the date, number downloaded, and any validation/API errors, plus totals. | Manual |
| AC-8 | **Secrets:** Repository search finds no API key; running without a key yields an actionable message (or documented DEMO_KEY fallback with warning). | Review (`git grep`) |
| AC-9 | **Docs:** `README.md` (run steps + assumptions) and `AI_NOTES.md` (all four required sections) are present and accurate. | Review |
| AC-10 | **History:** Git log contains multiple small, descriptive commits (target ≥ 8). | `git log` |
| AC-11 | **Tech:** Backend targets `net8.0`; UI (if present) is Angular; I/O is async. | Code review |
| AC-12 | **Bonus (if claimed):** Tests pass (`dotnet test`), Docker image builds and runs, UI displays thumbnails. | Automated / manual |

**Definition of Done:** all P1 requirements verified; AC-1–AC-11 pass; no known crash scenarios; documentation complete; code walkthrough prepared (§19).

---

## 15. Testing Requirements

### 15.1 Unit Tests (P3, strongly recommended)
**Date parser**
| Input | Expected |
|---|---|
| `02/27/17` | Valid 2017-02-27 |
| `June 2, 2018` | Valid 2018-06-02 |
| `Jul-13-2016` | Valid 2016-07-13 |
| `April 31, 2018` | Invalid (DATE_INVALID) |
| `2/29/2016` | Valid (leap year) |
| `2/29/2017` | Invalid (non-leap) |
| `13/01/17` | Invalid/unrecognized under US ordering |
| `  June 2, 2018  ` | Valid (whitespace trimmed) |
| `june 2, 2018` | Valid (case-insensitive) |
| `` / `abc` / `99/99/99` | Unrecognized/invalid, no exception |
| Ran under `de-DE`, `fr-FR` culture | Same results (culture-invariance) |

**API mapping:** full response → domain; empty `photos`; null/missing fields; extra unknown fields; malformed JSON; first-N selection ordering.
**Downloader/store:** skip when file exists; atomic temp→final; cleanup on failure; filename sanitization; traversal attempt rejected.
**Orchestrator:** partial failures isolated; totals correct; duplicate dates processed once.
**Resilience:** mocked `HttpMessageHandler` returning 429→200, 500×3, timeout, 404.

### 15.2 Integration Tests
- Web API tests with `WebApplicationFactory` and a stubbed NASA handler (no live calls in CI).
- A single opt-in live smoke test (excluded by default to respect rate limits).

### 15.3 Manual / Exploratory
- Run twice (idempotency), disconnect network mid-run, invalid key, read-only output folder, very large file of dates.

### 15.4 UI Tests (optional)
- Component tests for result rendering, error/empty/loading states.

---

## 16. Risks & Mitigations

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R-1 | `DEMO_KEY` rate limit exhausted during dev/review | High | Med | Request personal key; cache results/skip existing; retry with backoff; sequential/low concurrency; stubbed tests |
| R-2 | **CONFIRMED 2026-10-06:** the Mars Rover Photos endpoint on api.nasa.gov is down (see §22) | **Realized** | High | Graceful error reporting; DTO tolerance; mocked tests; document in README; verify endpoint early |
| R-3 | No photos exist for a given rover/date (e.g., rover inactive) | Med | Med | Treat as valid empty result; configurable rover; document A-3 |
| R-4 | Ambiguous date formats (MM/dd vs dd/MM) | Med | Med | Documented assumption A-2; explicit format list; tests |
| R-5 | Two-digit year misinterpretation | Low | Med | Deterministic pivot; test |
| R-6 | `img_src` over HTTP/redirects/hotlink restrictions | Med | Med | Follow redirects, upgrade to HTTPS, allow-list, retry |
| R-7 | Partial files from interrupted downloads | Med | Med | Temp file + atomic rename |
| R-8 | API key leakage via logs/URLs/commits | Med | High | Redaction, `.gitignore`, env vars, git history scan |
| R-9 | AI-generated code contains subtle defects (e.g., culture-dependent parsing, blocking async, missing disposal) | High | Med | Review checklist, tests, document in `AI_NOTES.md` |
| R-10 | Scope creep (UI, Docker) vs. time-box | Med | Med | Deliver P1 first; bonus afterwards; commit incrementally |
| R-11 | Filename collisions across cameras/photos | Low | Low | Use unique photo ID in name |
| R-12 | CORS/ports/Angular proxy friction in local run | Med | Low | Dev proxy config, documented ports |

---

## 17. Open Questions & Clarifications

| ID | Question | Proposed default |
|---|---|---|
| OQ-1 | **RESOLVED 2026-10-06: YES, in scope.** Is the Angular UI mandatory or optional? Brief lists UI as optional output yet mandates Angular for UI. | Deliver console + JSON API as baseline and a minimal Angular UI; confirm with reviewer |
| OQ-2 | Preferred rover? | Curiosity (active throughout the sample dates) |
| OQ-3 | Slash format order (US vs. day-first)? | US `MM/dd/yy` |
| OQ-4 | Should "first 3–5 photos" be exactly 5, or any count in range? | Default 5, configurable |
| OQ-5 | Should the app be a console tool, a web API, or both? | Shared application layer with console runner + Web API |
| OQ-6 | **RESOLVED 2026-10-06: project key supplied via gitignored settings; no DEMO_KEY; refuse if missing.** Behavior when the API key is absent: fail or fall back to `DEMO_KEY`? | Fail with guidance; opt-in fallback flag |
| OQ-7 | **Unanswered; default retained.** Exit code policy for partial success? | `0` if run completed (errors reported in summary); non-zero only for fatal errors |
| OQ-8 | Is the photo `earth_date` guaranteed equal to the requested date in responses? | Verify live; log mismatch |
| OQ-10 | The specified API is unavailable — provider-fallback approach in §22? | **APPROVED 2026-10-06**: primary adapter per brief + fallback adapter on NASA raw-image feed |
| OQ-9 | Is a `sol`-based fallback desired when `earth_date` returns nothing? | No (out of scope) |

---

## 18. Traceability Matrix

| Brief statement | Requirement(s) | Acceptance |
|---|---|---|
| Read dates from text file | FR-1.x | AC-2 |
| Parse & validate dates, multiple formats | FR-2.x, NFR-1 | AC-2 |
| Handle invalid dates without crashing; log errors | FR-2.5, FR-2.10, §11 | AC-2, AC-6 |
| Call API using `earth_date`, 3–5 photos, any rover | FR-3.x | AC-3 |
| Save to `photos/<date>/` | FR-4.1–4.2 | AC-4 |
| Don't re-download existing images | FR-4.3, NFR-3 | AC-5 |
| Handle network failures gracefully | FR-3.7–3.8, FR-4.4–4.7, NFR-4 | AC-6 |
| Output date, count, errors (console/JSON/UI) | FR-5.x, FR-7.x | AC-7 |
| AI_NOTES.md | DEL-4 | AC-9 |
| .NET 8 backend | C-1 | AC-11 |
| Angular UI | C-2, FR-7.x | AC-11 |
| async/await | C-3, NFR-14 | AC-11 |
| No hardcoded API keys | C-4, FR-6.3, SEC-1 | AC-8 |
| README with run steps & assumptions | DEL-3 | AC-9 |
| Multiple small commits | DEL-5, §13.1 | AC-10 |
| Bonus: unit tests | §15, DEL-6 | AC-12 |
| Bonus: error handling & logging | §11, NFR-9 | AC-6 |
| Bonus: thumbnails UI | FR-5.5, FR-7.4 | AC-12 |
| Bonus: Dockerfile | DEL-7, NFR-10 | AC-12 |
| Walkthrough expectations | §19 | Review |

---

## 19. Code Walkthrough Readiness

The submission SHOULD allow the author to explain the following, each backed by evidence in the repo.

| Topic | Expected talking points |
|---|---|
| **Project structure** | Layered/clean architecture rationale; dependency direction; separation of host vs. logic; testability |
| **AI usage** | Tools used; how prompts were refined; review/verification practices (tests, reading docs, running code); where AI was rejected or corrected (mirrors `AI_NOTES.md`) |
| **Date parsing decision** | `TryParseExact` + explicit format list + invariant culture; two-digit year handling; why not free-form `DateTime.Parse`; US-order assumption; April 31 handling |
| **Error-handling strategy** | Result objects over exceptions; per-item isolation; error code catalogue; fatal vs. non-fatal |
| **API failure handling** | Retry/backoff/jitter for transient; no retry for permanent; rate-limit awareness; timeouts; cancellation |
| **Duplicate avoidance** | Deterministic filenames from photo ID; existence + non-zero-size check; temp-file atomic rename |
| **Code quality** | Naming, SRP, DI, immutability (records), nullable annotations, no magic strings (constants/options), small methods |
| **Improvements** | See §20 |

---

## 20. Future Enhancements (Production Roadmap)

1. Persistent metadata store (SQLite/PostgreSQL) for photo catalog, run history, and checksum-based dedup.
2. Content-hash verification (SHA-256) and size/ETag validation for integrity.
3. Background job queue (Hangfire/Worker Service) with progress streaming (SignalR/SSE) to the UI.
4. Distributed/blob storage (Azure Blob/S3) and CDN thumbnails; image thumbnail generation.
5. Response caching and conditional requests; centralized rate-limit governor (token bucket).
6. Polly circuit breaker; health checks; OpenTelemetry tracing and metrics; dashboards/alerts.
7. Pagination support (`page` param) beyond the first N; filtering by camera.
8. Configurable locale-aware parsing and user-selected date order; NLP-style fuzzy dates.
9. CI/CD (build, test, vulnerability scan, container publish), IaC, secrets via Key Vault.
10. Authentication/authorization, API versioning, OpenAPI documentation, rate limiting of own API.
11. Broader test pyramid: contract tests against API schema, mutation testing, E2E (Playwright).
12. Accessibility audit and i18n for the UI.

---

## 21. Glossary

| Term | Definition |
|---|---|
| Earth date | Calendar date on Earth used by `earth_date` query parameter |
| Sol | A Martian solar day (~24h 39m); rover mission day counter |
| Rover | Curiosity, Opportunity, or Spirit |
| Idempotent | Re-running yields the same end state without redundant work |
| Transient fault | Temporary failure likely to succeed on retry (429, 5xx, timeout) |
| Atomic write | Write to temp file then rename so readers never see partial content |
| DTO | Data Transfer Object mirroring external JSON |
| SSRF | Server-Side Request Forgery |
| SRS | Software Requirements Specification |

---
## 22. Verification Findings (live probe, 2026-10-06)

Probes were made with the project's NASA key (stored only in the gitignored `.env`; never in this repo).

| # | Finding | Evidence |
|---|---|---|
| F-1 | API key is **valid**; rate limit is **2000 requests/hour** (`X-Ratelimit-Remaining` header) — not the DEMO_KEY limits assumed in §5.3 | Header observed; APOD and NEO endpoints return 200 |
| F-2 | **The Mars Rover Photos API is unavailable.** Every route under `https://api.nasa.gov/mars-photos/api/v1/` (photos by `earth_date`/`sol`, `latest_photos`, `manifests`, `rovers`) for curiosity/opportunity/spirit returns **HTTP 404 HTML "No such app"** — the Heroku backend no longer exists. The community mirror `mars-photos.herokuapp.com` returns the same. | 9/9 date×rover calls and 6 route variants |
| F-3 | api.nasa.gov can return transient **HTTP 500** (observed once on APOD, then 200), supporting the retry requirement (FR-3.8) | Observed |
| F-4 | A working alternative exists: NASA's public raw-image feed `https://mars.nasa.gov/rss/api/?feed=raw_images&category=msl&feedtype=json&sol={sol}` (Curiosity). It needs **sol**, not `earth_date`, and each image has `date_taken_utc` and `image_files{small,medium,large,full_res}` (HTTP URLs on `mars.nasa.gov` / `mars.jpl.nasa.gov`). Sample dates map to sols 1621 (2017-02-27), 2069 (2018-06-02), 1398 (2016-07-13); neighbouring sols return 96–343 images each. | Live calls; no key needed |
| F-5 | Brief conflict: it mandates "NASA Mars Rover Photos API" and `earth_date`, which no longer works. | F-2 |

### 22.1 Resulting Requirements
| ID | Requirement |
|---|---|
| FR-3.11 | Photo retrieval SHALL sit behind a provider port (`IPhotoProvider`) with a configurable provider. |
| FR-3.12 | The **primary provider** SHALL implement the Mars Rover Photos contract in §8.1 exactly (so it works unchanged if the service returns). |
| FR-3.13 | A **fallback provider** SHALL use the raw-image feed (F-4): convert Earth date → sol (Curiosity landing 2012-08-06 05:17:57 UTC, sol length 88775.244 s), query sol−1…sol+1, keep only images whose `date_taken_utc` date equals the requested date, take the first N. |
| FR-3.14 | On primary 404/5xx after retries the system SHALL fall back automatically, report which provider served each date, and log the reason. Failure of both providers SHALL be a per-date `API_UNAVAILABLE` error. |
| FR-3.15 | Non-JSON (HTML) responses SHALL be classified `API_BAD_RESPONSE`, never thrown as parse exceptions. |
| FR-3.16 | Image hosts allow-list SHALL include `mars.nasa.gov`, `mars.jpl.nasa.gov`; `http://` image URLs SHALL be upgraded to `https://` when reachable, else downloaded over http with a logged warning. |
| NFR-15 | Tests SHALL use recorded fixtures of both providers; the unavailability of the real service is itself a required test case. |
| SEC-10 | The API key SHALL live only in gitignored `.env` / user-secrets / environment (`NASA__ApiKey`); `.env.example` documents the name with a placeholder. A pre-commit gate SHALL block commits containing key-like strings. |

### 22.2 Corrected Assumptions
- §5.3: rate limit with the project key is 2000/h; DEMO_KEY limits no longer drive the design.
- A-3: Curiosity remains the default; Opportunity/Spirit are unsupported by the fallback feed and report `API_UNAVAILABLE`.

---
*End of document.*

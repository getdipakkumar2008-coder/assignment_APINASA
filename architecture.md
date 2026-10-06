# Mars Rover Photo Downloader — Architecture

This document describes the system architecture, component responsibilities, how the frontend calls the backend, how the backend is divided into layers that call third-party (NASA) APIs, and the end-to-end application flow.

## 1. High-Level Architecture Diagram

```text
┌───────────────────────────────────────────────────────────────────────────┐
│                         FRONTEND (Angular 22 UI)                         │
│  src/marsphotos-ui  — standalone, strict TypeScript                      │
│  - Lets user trigger a run, view status, browse results & thumbnails     │
│  - Served at http://localhost:4200                                       │
└───────────────────────────────┬──────────────────────────────────────────┘
                                 │ HTTPS REST (JSON) over HTTP
                                 │
                                 │   POST /api/runs            (start a run)
                                 │   GET  /api/runs/latest     (poll status/results)
                                 │   POST /api/runs/cancel     (cancel in-flight run)
                                 │   GET  /photos/*            (serve downloaded images)
                                 │   GET  /health              (liveness)
                                 │   GET  /openapi.yaml, /swagger (API docs)
                                 ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                    BACKEND — src/MarsPhotos.Api                          │
│  ASP.NET Core Minimal API, http://localhost:5180                         │
│  Thin HTTP host: maps routes → invokes the shared use case, maps         │
│  results back to HTTP responses (status codes, JSON payloads)            │
└───────────────────────────────┬──────────────────────────────────────────┘
                                 │ in-process call
                                 ▼
┌───────────────────────────────────────────────────────────────────────────┐
│        src/MarsPhotos.Application (Use Case / Orchestration Layer)       │
│  - ProcessDatesUseCase — core orchestration, shared by CLI and API       │
│  - Date parser (DateOnly.TryParseExact, strict format list)             │
│  - Provider chain — tries primary source, auto-falls back on failure    │
│  - Ports (interfaces): IPhotoProvider, IFileStore, IDownloader, etc.     │
│  - Output renderers (console text / JSON summary)                       │
│  Depends only on MarsPhotos.Domain (no infra/HTTP knowledge here)        │
└───────────────────────────────┬──────────────────────────────────────────┘
                                 │ implemented by
                                 ▼
┌───────────────────────────────────────────────────────────────────────────┐
│       src/MarsPhotos.Infrastructure (Adapters / External I/O Layer)      │
│  - NASA HTTP clients (Mars Rover Photos API + Raw Image Feed)            │
│  - HTTP resilience: retry with exponential backoff, jitter, Retry-After │
│  - Downloader: atomic .part → move, content-type check, size cap        │
│  - File store: photos/<yyyy-MM-dd>/ deduped by photo id                 │
│  - URL policy: HTTPS only + exact host allow-list                       │
│  - Configuration: NASA__ApiKey from env/.env, retry/timeout settings     │
└───────────────┬───────────────────────────────┬──────────────────────────┘
                 │ primary                       │ automatic fallback
                 ▼                               ▼ (on 404 / outage)
┌───────────────────────────────┐   ┌─────────────────────────────────────┐
│ 3rd-party: NASA Mars Rover     │   │ 3rd-party: NASA Raw Image Feed       │
│ Photos API                     │   │ (Curiosity only, public, keyed by    │
│ api.nasa.gov/mars-photos/...   │   │ Mars "sol"; Earth date → sol conv.)  │
└───────────────────────────────┘   └─────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────────────────┐
│                src/MarsPhotos.Domain (Core / innermost layer)            │
│  Records, error codes, rover mission date ranges, sol conversion.        │
│  Zero dependencies — referenced by Application and Infrastructure.       │
└───────────────────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────────────────┐
│       src/MarsPhotos.Cli (alternate thin host, no HTTP/UI involved)      │
│  Console runner — reads dates.txt, invokes the same ProcessDatesUseCase, │
│  prints a summary table. Exercises the exact same backend logic as API. │
└───────────────────────────────────────────────────────────────────────────┘
```

**Dependency rule (Clean Architecture):**
`Domain ← Application ← Infrastructure`, with **Cli** and **Api** as interchangeable, thin "host" entry points that both sit on top of the same `Application` use case — so CLI and API behavior can never diverge.

## 2. How the Frontend Calls the Backend

| Step | Caller | Call | Purpose |
|---|---|---|---|
| 1 | Angular UI | `POST /api/runs` | Start a new run (reads configured dates, kicks off processing) |
| 2 | Angular UI | `GET /api/runs/latest` (polling) | Retrieve current status / final summary of the run |
| 3 | Angular UI | `POST /api/runs/cancel` | Cancel an in-flight run |
| 4 | Angular UI | `GET /photos/{date}/{file}` | Load downloaded photo thumbnails/images for display |
| 5 | Angular UI | `GET /health` | Backend health check |
| 6 | Dev/Docs | `GET /swagger`, `GET /openapi.yaml` | Interactive/contract API documentation |

- Communication is plain JSON over HTTP(S); the API only allows **one run at a time** — a second concurrent `POST /api/runs` returns **HTTP 409**.
- The API is a *thin* layer: it does no business logic itself — it validates the HTTP request, invokes `ProcessDatesUseCase` in the Application layer, and maps the returned result to an HTTP response.

## 3. How the Backend Is Divided Into Components

| Layer | Project | Responsibility | Talks to |
|---|---|---|---|
| Host (HTTP) | `MarsPhotos.Api` | ASP.NET Core minimal API; routes HTTP requests to the use case | Application layer |
| Host (Console) | `MarsPhotos.Cli` | Console entry point; same use case, prints to stdout | Application layer |
| Orchestration | `MarsPhotos.Application` | `ProcessDatesUseCase`, date parsing, provider-chain/fallback logic, ports (interfaces) | Domain, Infrastructure (via interfaces) |
| Adapters | `MarsPhotos.Infrastructure` | NASA HTTP clients, retry/resilience, downloader, file store, URL allow-list, configuration | 3rd-party NASA APIs, filesystem |
| Core | `MarsPhotos.Domain` | Records, error codes, mission date ranges, sol conversion — no dependencies | (nothing; pure domain) |

Each layer only depends on the layer(s) inward of it. `Infrastructure` implements interfaces (`ports`) defined in `Application`, which keeps the orchestration logic decoupled from concrete HTTP/NASA details (dependency inversion).

## 4. How the Backend Calls Third-Party (NASA) APIs

1. `Application`'s **provider chain** first requests photos from the **primary** provider — the documented **NASA Mars Rover Photos API** (`api.nasa.gov/mars-photos/api/v1/...`), as specified in the original brief.
2. If that call fails with **HTTP 404** (the service is currently retired) or is otherwise unavailable, the provider chain **automatically falls back** to NASA's **public Raw Image Feed** (Curiosity rover only).
   - Since the Raw Image Feed is keyed by Martian **sol** (not Earth date), the `Domain` layer's sol-conversion logic converts the requested Earth date to the corresponding sol before querying.
3. All outbound HTTP calls to NASA pass through `Infrastructure`'s resilience layer:
   - **Transient failures** (timeouts, network errors, HTTP 408/429/5xx) → retried with exponential backoff, jitter, and respect for `Retry-After`.
   - **Permanent failures** (400/401/403/404) → never retried.
4. Every result returned to the caller (CLI or API/UI) records **which source served it** (`MarsRoverPhotos` vs `RawImageFeed`) and **why a fallback occurred**, so the behavior is fully transparent.
5. Downloaded photo URLs are checked against a strict **host allow-list** (`mars.nasa.gov`, `mars.jpl.nasa.gov`) and must be **HTTPS** before any download is attempted — this is enforced in `Infrastructure`'s URL policy, independent of which provider returned the URL.

## 5. End-to-End Application Flow

```text
1. User opens the Angular UI (http://localhost:4200) and clicks "Run"
        │
        ▼
2. UI sends POST /api/runs to MarsPhotos.Api
        │
        ▼
3. Api host forwards the request into ProcessDatesUseCase (Application layer)
        │
        ▼
4. Use case reads & validates the input dates (dates.txt or request payload)
     - Invalid dates (e.g. "April 31, 2018") → reported as InvalidDate, not looked up
     - Dates outside a rover's mission range → reported, not looked up
        │
        ▼
5. For each valid date, the Provider Chain runs:
     a. Try NASA Mars Rover Photos API (primary, documented contract)
     b. On HTTP 404 / unavailable → fall back to NASA Raw Image Feed
        (Earth date converted to sol via Domain logic)
        │
        ▼
6. Infrastructure applies cross-cutting policies on every outbound call:
     - Retry w/ exponential backoff + jitter for transient errors
     - No retry for permanent errors (400/401/403/404)
     - URL allow-list + HTTPS enforcement before any download
        │
        ▼
7. Downloader (Infrastructure) fetches each photo:
     - Checks if the photo id already exists locally → skip duplicate (Skip++)
     - Otherwise streams to photos/<yyyy-MM-dd>/<id>.jpg.part
     - Verifies content-type, non-empty body, size cap
     - Atomically renames .part → final file only once fully verified (New++)
        │
        ▼
8. Use case aggregates a run summary per date:
     Status, Source used (MarsRoverPhotos/RawImageFeed), New/Skip/Fail counts,
     and any fallback/error notes
        │
        ▼
9. Api host exposes the summary via GET /api/runs/latest
        │
        ▼
10. UI polls /api/runs/latest, renders the results table and photo thumbnails
     (images loaded via GET /photos/<date>/<file>)
        │
        ▼
11. Re-running the same dates is idempotent: New:0, Skip:<count> — nothing
     is downloaded twice, because existence is checked before any request.
```

## 6. Component Interaction Summary

- **Frontend (Angular) ⇄ Backend (Api):** stateless REST/JSON calls only; UI never talks to NASA or the filesystem directly — everything is mediated by the API.
- **Api ⇄ Application:** the API host is a thin adapter; all business rules (parsing, fallback logic, run summary) live in `ProcessDatesUseCase`.
- **Application ⇄ Infrastructure:** Application defines interfaces (ports) such as `IPhotoProvider`, `IDownloader`, `IFileStore`; Infrastructure provides concrete implementations (NASA HTTP clients, resilient downloader, local file store). This inversion lets Application stay testable and free of HTTP/filesystem concerns.
- **Infrastructure ⇄ 3rd-party NASA APIs:** two providers (Mars Rover Photos API primary, Raw Image Feed fallback) behind a common `IPhotoProvider` interface, chained with automatic failover, retry policy, and URL allow-listing.
- **Cli ⇄ Application:** identical integration path as Api, proving the same use case drives both console and web entry points with no divergent logic.
- **Domain:** a dependency-free core consumed by both Application (business rules) and Infrastructure (e.g., sol conversion needed for the fallback provider).

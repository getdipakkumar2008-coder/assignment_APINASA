# Quickstart / Validation Guide

Prerequisites: .NET 8 SDK (8.0.204+), Node 20+ with Angular CLI, a NASA key. Commands run from the repo root. Type details are in [data-model.md](data-model.md); wire formats in [contracts/](contracts/).

## 1. Configure the key (never committed)
```bash
cp .env.example .env          # then edit .env: NASA__ApiKey=<your key>
```
`.env` is gitignored. Alternative: `dotnet user-secrets set Nasa:ApiKey <key> --project src/MarsPhotos.Api`.

## 2. Build and test
```bash
dotnet build
dotnet test
npm --prefix src/marsphotos-ui test -- --watch=false
```
Expect all green. Covers AC-2 (parsing), AC-3 / AC-6 (providers including the HTML 404, 429, 5xx, timeout cases), AC-5 (idempotency), AC-8 (no secret in output).

## 3. Console run (AC-1 to AC-7)
```bash
dotnet run --project src/MarsPhotos.Cli
```
Expected: the three valid dates each yield 3–5 photos in `photos/2017-02-27/`, `photos/2018-06-02/`, `photos/2016-07-13/`; `April 31, 2018` is reported `InvalidDate`; the source column shows `RawImageFeed` while the primary is down; exit code 0.

## 4. Idempotency (AC-5)
```bash
dotnet run --project src/MarsPhotos.Cli      # second run
```
Expected: `New 0`, `Skip` equals the previous total; file timestamps unchanged.

## 5. Failure drills (AC-6)
- Unset the key: exit 1 with guidance (FR-025).
- Disconnect the network mid-run: the run completes, errors are listed, and no `.part` or zero-byte images remain.
- Delete one photo: only that photo is downloaded on the next run.

## 6. API and UI (User Story 6)
```bash
dotnet run --project src/MarsPhotos.Api                 # http://localhost:5080
npm --prefix src/marsphotos-ui start                    # http://localhost:4200 (proxies /api and /photos)
```
Expected: click **Run**; per-date cards show status badges, counts, errors and lazy thumbnails; empty, loading and error states are visible; everything is keyboard operable.

## 7. Secret hygiene (AC-8, SC-007)
```bash
git grep -n "$(sed -n 's/^NASA__ApiKey=//p' .env)" || echo clean
```
Expected: `clean`. The key is also absent from console output, logs, and API/UI responses.

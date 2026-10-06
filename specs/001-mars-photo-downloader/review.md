# Code and Security Review Record

Reviewer: the project's `code-reviewer` agent (independent, read-only, run on the committed code on 2026-10-06), plus the author's own security checks. Findings use CRITICAL / HIGH / MEDIUM / LOW / INFO. Gate (constitution XII): CRITICAL and HIGH must be fixed before completion.

**Result: 0 CRITICAL, 0 HIGH.** 3 MEDIUM and 7 LOW/INFO findings; all MEDIUM fixed, most LOW fixed, two accepted with a written reason. Each fix has a regression test.

## Findings from the independent review

| # | Sev | Finding | Disposition | Evidence |
|---|---|---|---|---|
| 1 | MEDIUM | Another website's page could start or cancel a run by sending a simple cross-origin `POST` (CORS only hides the response) | **Fixed.** State-changing requests with a foreign `Origin` get 403 `FORBIDDEN_ORIGIN`; the UI origin and non-browser clients still work | `SecurityEndpointTests` (6 cases) |
| 2 | MEDIUM | A non-JSON body (`text/plain`) would likely surface as a generic 500 instead of a 4xx | **Fixed.** 415 `UNSUPPORTED_MEDIA_TYPE` problem response | `SecurityEndpointTests` |
| 3 | MEDIUM | Duplicate ids or ids that sanitize to the same file name could cause concurrent writes to one file, delete another writer's `.part`, or silently lose a photo; delete-then-move was not atomic | **Fixed.** Provider duplicates are de-duplicated; lossy ids get a deterministic hash suffix so different ids never share a name; a writer deletes only a `.part` it created; the final step is one overwrite-move that refuses to replace a valid file | `StoreConcurrencyTests` (9 cases incl. locked `.part`, finished-by-someone-else, 20 parallel writers, 8 writers on one name) |
| 4 | LOW | Provider response size cap checked only against `Content-Length` | **Fixed.** `MaxResponseContentBufferSize` (20 MB) on provider clients | configuration change |
| 5 | LOW | Provider clients auto-followed redirects while the key is in the URL | **Fixed.** `AllowAutoRedirect=false`; a 3xx answer is a bad response | `Http_failures_map...` cases for 301/302 |
| 6 | LOW | `.env` search could climb to the filesystem root (a stray `/.env` in a container) | **Fixed.** The search stops at the repository/solution root and never looks above it | `Never_loads_an_env_file_from_above_the_repository_root` |
| 7 | LOW | A cancel arriving while the dates file was being read could produce a 500 | **Fixed.** 409 `RUN_CANCELLED` | handled in `Program.cs`; **no direct test** because the race window is too narrow to force reliably (the cancel endpoint itself is tested) |
| 8 | LOW | After a page reload during a run the UI shows Idle; pressing Run gets a 409 | **Accepted, documented** under known limitations. `Latest` is now read/written under a lock. A status endpoint would fix it | README |
| 9 | LOW | UI photo links worked only through the dev proxy | **Fixed.** Photo URLs honour `API_BASE_URL` | `results.spec.ts` |
| 10 | LOW | Test quality: a misleading test name, traversal test asserted only "not 200", a pre-cancelled-token test, a timing-sensitive backoff bound | **Fixed.** Renamed, asserts 404/400, added an in-flight cancellation test, bound loosened to only guard against unbounded backoff | tests |
| 11 | INFO | Symlinks inside `photos/` are not checked | **Accepted.** Needs local write access to the output folder; out of scope for this tool | - |
| 12 | INFO | `RunTotals.Valid` counts every line with a parsed date (including outside-range and cancelled) | **Accepted.** Matches the spec wording ("valid" means a real calendar date) | - |

Reviewer's "checked and fine" list (summarised): secrets never reach logs, responses or the image; URL allow-list and per-hop redirect checks; path traversal and the images-only static endpoint; atomic writes and size/type validation; retry rules (no retry on 4xx, `Retry-After`, caller cancellation not retried); disposal and cancellation propagation; no blocking async; Angular has no `innerHTML` or sanitizer bypass.

## Defects found by the author's own testing and running (not by the reviewer)

| Found by | Defect | Fix |
|---|---|---|
| Live probe before coding | The Mars Rover Photos API named in the brief is gone (HTTP 404 "No such app") | Provider chain with automatic fallback (decision OQ-10) |
| Live research while planning | Filtering fallback photos by capture time returns **0 photos for 2018-06-02** | Select by the sol that corresponds to the date (decision D-1) |
| Unit test (red) | `../x.jpg` stays inside the root but escapes the date folder, so `EnsureInside` accepted it | `Resolve()` requires the parent to be exactly the date folder |
| API test | `notes.txt` was served from `/photos` because `.txt` is a known MIME type | Explicit image-only content-type map |
| Running the real API | `dotnet run --project` makes the project folder the working directory, so `dates.txt` and `photos/` resolved to the wrong place (all tests used absolute paths) | `ProjectPaths` resolves against the solution root |
| Running the real API | Port 5080 was used by another local app | Default port 5180 |
| Running the container | The documented `docker run ... dotnet cli/...` appended arguments to the API entry point and hung | Documented `--entrypoint dotnet` |
| Security scan | Test projects pulled `System.Net.Http 4.3.0` and `System.Text.RegularExpressions 4.3.0` (both High advisories) transitively | Pinned patched versions (test-only, never shipped) |
| Honesty check | Error text and CLI help promised `user-secrets` support that was never wired up | Removed the claim; documented env var / `.env` only |
| Axe negative test | (verification) removing image alt text correctly failed with `image-alt (critical)` | test proven effective |

## Security review checklist

| Check | Result |
|---|---|
| `dotnet list package --vulnerable --include-transitive` (every project) | No vulnerable packages after pinning the two test-only packages |
| `npm audit` (all dependencies) | 0 vulnerabilities |
| Key absent from tracked files and from all git history | `scripts/check-no-secrets.sh` and `.ps1`: clean; `RepoSecretScanTests` guards it in CI; negative test proved it detects a planted key |
| Key absent from logs, API responses, UI, Docker image | Tests (`NoSecretInLogsTests`, `RunsEndpointTests`, `StaticPhotosTests`), live API log grep, `docker history` grep: none |
| `.env` ignored by git and Docker | `.gitignore`, `.dockerignore`, `RepoSecretScanTests` |
| Input validation | Date parser (bounded regexes, explicit formats), line and file limits, API request limits |
| SSRF / untrusted URLs | Exact host allow-list, HTTPS only, redirects re-checked per hop, no credentials in URLs, size and type limits |
| CORS and cross-site requests | One allowed origin; foreign-origin POSTs rejected |
| Static files | Images only, no traversal, `.part` and other types are 404 |
| Container | Multi-stage, non-root user, key passed at run time |

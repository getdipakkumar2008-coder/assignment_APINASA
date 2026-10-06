# AI Notes

How AI was used on this project, what it got wrong, and what a human decided. Written from the actual session, not reconstructed.

## 1. Tools

| Tool | Use |
|---|---|
| **Claude Code CLI** (model: Claude Sonnet 5.5) | The implementing agent: requirements analysis, specification, plan, tasks, all code and tests, reviews, documentation, git commits |
| **GitHub Spec Kit 1.0.13** (Claude integration + git extension) | The process: `constitution` → `specify` → `clarify` → `plan` → `checklist` → `tasks` → `analyze` → `implement`. Its skills live in `.claude/skills/speckit-*` |
| **Project agents** (`.claude/agents/`) | Five role definitions: business analyst, solution architect, senior .NET developer, QA engineer, code reviewer. **Only the code reviewer was actually spawned** (as an independent subagent, see `review.md`). The other roles were applied inline by the main agent, not as separate agents |
| **Project skills** (`.claude/skills/`) | `project-conventions`, `nasa-photo-providers` (the dead-API / fallback / sol facts), `requirements-traceability`, `ai-code-review` (checklist used for every review) |
| **Hooks** (`.claude/hooks/`) | `guard_implementation.py` blocks edits under `src/` and `tests/` until the constitution, spec, plan and tasks exist; `pre_commit_gate.py` blocks commits that stage `.env`, contain a key-like value, or fail build, .NET tests or UI tests. It blocked one of my own commands mid-session, which showed it was live |
| **Microsoft .NET agent-skills plugins** (`dotnet`, `dotnet-aspnetcore`, `dotnet-msbuild`, `dotnet-test`, `dotnet-diag`) | Installed and enabled at project scope. **Honest note:** I did not observe a specific plugin skill being invoked by name; the .NET work relied on the model's own knowledge plus the project skills, so I cannot claim measurable plugin influence |
| axe-core, Vitest, xUnit, `WebApplicationFactory`, headless Chrome | Verification: accessibility checks, tests, a real-browser screenshot. The Claude-in-Chrome extension was not connected, so headless Chrome was used instead |

## 2. The prompts that worked best

1. **The governing prompt (written by the project owner).** A long "Enterprise Spec-Driven Development" prompt that fixed the lifecycle (requirements → constitution → spec → clarify → plan → checklist → tasks → analyze → implement → converge), the agent roles, and rules such as "Do not code immediately", "When requirements conflict: STOP", and "Claude-generated code must be treated as untrusted until reviewed". It worked because it turned vague "build the app" intent into gates that could be checked. Several of the best catches below happened *because* a gate forced a pause.
2. **"Analyse once again this entire task."** Said after the key was supplied. It made me probe the real NASA API with the real key *before* planning. That is how the central surprise was found: the Mars Rover Photos service named in the brief returns HTTP 404 "No such app". Without this prompt the project would have been built against a dead service.
3. **"Approve D-1, proceed" / "Approve" (after a precise question).** I never asked open questions; I asked one decision at a time with a recommendation and the evidence (for example "keep the UTC-date rule and get 0 photos for 2018-06-02, or select by sol"). Short, specific approvals kept the human in control of business decisions while the agent did the mechanical work.

The prompt I used on the reviewer subagent also paid off: it named the checklist (`ai-code-review`), demanded failure scenarios and asked it to say when it could not confirm a finding. It returned 0 CRITICAL/HIGH and 3 MEDIUM, each verified against the code.

## 3. Examples where the AI's work was wrong or incomplete (and how it was found)

| # | What the AI produced | How it was caught | Fix |
|---|---|---|---|
| 1 | **A requirement that could not work.** My own requirements (and the plan) said to keep fallback photos "taken on the requested date" (`date_taken_utc`) | Live research during planning: 25 / **0** / 187 photos for the three sample dates. The original service's `earth_date` means "the Earth date assigned to a sol", not the capture time | Decision D-1 (human-approved): select by the sol that corresponds to the date |
| 2 | **Path traversal that stays inside the root.** The store checked "path is under the photos root", so `2017-02-27/../evil.jpg` passed although it escapes the date folder | A test I wrote first: `Traversal_in_a_file_name_is_rejected` failed for 3 inputs | `Resolve()` requires the file's parent to be exactly the date folder |
| 3 | **Config that only worked in tests.** Relative `dates.txt` / `photos` paths resolved against the working directory. `dotnet run --project` makes the *project folder* the working directory for ASP.NET, so the real API reported "dates.txt not found" while all 31 API tests passed (they used absolute paths) | Running the real API against the real service | `ProjectPaths` resolves against the solution root; CLI-typed paths stay relative to the caller |
| 4 | **"Images only" that was not.** `ServeUnknownFileTypes=false` still served `notes.txt` because `.txt` is a *known* type | Test `Part_files_and_unknown_file_types_are_not_served` | Explicit allow-list of `.jpg/.jpeg/.png` |
| 5 | **A dishonest promise.** Error text and CLI help said the key could come from `dotnet user-secrets`, but no project had a `UserSecretsId` | A pre-publication consistency check of messages against behaviour | Claim removed |
| 6 | **A hanging "how to run" command.** The Dockerfile comment said `docker run ... marsphotos dotnet cli/MarsPhotos.Cli.dll`; with an `ENTRYPOINT` that appends arguments to the API, so it started the server and waited | Actually running the documented command | Documented `--entrypoint dotnet` |
| 7 | **Vulnerable dependencies pulled in by test templates** (`System.Net.Http 4.3.0`, `System.Text.RegularExpressions 4.3.0`, both High) | `dotnet list package --vulnerable --include-transitive` per project (the solution-level form printed usage in this SDK) | Pinned patched versions |
| 8 | **Smaller slips of mine:** a test expectation for `é日本` of two underscores (it is three characters); a test that asserted a rule my own loader did not have (an `.env` in a temp folder, while the author's home directory is itself a git repository); one verification count written as 333 passed when it was 332 passed + 1 skipped | Red tests, a failing test run, and re-reading numbers against output | Fixed, and the count corrected |
| 9 | **Shell tooling trap:** this environment's shell collapsed double backslashes in heredocs, silently corrupting C# escapes and regexes (twice producing code that did not compile or patches that did not apply) | Compiler errors and a patch script that asserted its target text | Files with backslashes are written with the file-writing tool or built with `chr(92)` |

| 10 | **Culture-dependent number parsing** slipped past my own "invariant culture" rule. `PhotoOrdering`, the CLI `--count` and the quota header used `long.TryParse`/`int.TryParse` with the current culture, so an id like `-5` counted as numeric on one machine and not on another (for example `sv-SE`, whose minus sign is U+2212) and photos could be chosen in a different order | The `/ai-code-review` checklist (a targeted search for `Parse`/`TryParse` without a culture) | Digits-only parsing with `CultureInfo.InvariantCulture` in all three places |
| 11 | **A test that could not fail.** My first regression test for #10 used ids containing `1e2`, so the ids were never "all numeric" in any culture and the old buggy code would have passed it too | A mutation check: I temporarily restored the old parsing and the test still passed | Rewrote it with `10, -5, 9` (numeric in one culture, not in another) and re-ran against the old code: 4 of 4 cultures now fail, as they should |
| 12 | **A swallowed error in the UI.** If the API was down or misrouted when the page loaded, the "latest run" call failed silently and the page just said "Ready" | The checklist's "swallowed exceptions" item (`error: () => {}`) | The error is now shown as an alert with guidance, with a test |
| 13 | **A mock hiding real configuration.** Downloader and provider tests use fake handlers, so the no-redirect setting on the real HTTP clients (the API key is in the query string) was never exercised | The checklist's "mocks hiding real behavior" item, and an earlier reviewer note | A test that inspects the real registered handler chain for all three clients |
| 14 | **A 204 "no run yet" that browsers may cache**, and a leftover 404-to-null fallback in the UI that would have hidden a wrong base URL as "no run yet" | `/code-review` on the 204 change | `Cache-Control: no-store` on all `/api` responses; the fallback removed |

The independent review (`review.md`) then found 3 further MEDIUM issues the author's own tests had not: a cross-site `POST` that could start a run, a non-JSON body producing a 500, and file-name collisions / concurrent writes to one file.

## 4. Significant changes after the AI generated code

- **Providers**: the single NASA client in the brief became a provider chain (primary + fallback) with a recorded reason per date.
- **Resilience pipeline** moved from the user-story phase to the foundational phase after `/speckit-analyze` showed the downloader and the clients both depended on it.
- **Commit rule**: `/speckit-analyze` pointed out that "commit the failing test alone" contradicts a hook that blocks commits when tests fail. Tests are now committed together with the code that makes them pass.
- `IClock` was dropped for the built-in `TimeProvider`; `Redaction` moved from Infrastructure to Application once the output renderers needed it; the store port gained `EnsureWritable()`, `ProviderResult` gained provider/fallback fields, `DateResult` gained a `Note`.
- Default API port changed from 5080 to 5180 after discovering another local app on 5080.
- File names: from `Sanitize(id)` to `StemFor(id)` (hash suffix for lossy ids) after the review.
- Photo URLs in the UI follow `API_BASE_URL` after the review.

## 5. Important architectural decisions (human-approved where marked)

- Layered Domain / Application / Infrastructure with two thin hosts (CLI, API) over one use case; no database, queue or cache.
- **OQ-10 (approved):** fall back to NASA's raw-image feed when the original service is unavailable.
- **D-1 (approved):** the fallback maps the Earth date to the Curiosity sol (noon UTC, 88 775.244 s per sol).
- Errors are values with stable codes; one failure never stops a batch; only a missing key, unreadable input or unwritable output stop a run.
- Idempotency through deterministic names, existence check before any request, and `.part` + atomic move.
- Secrets only in the environment / git-ignored `.env`, redacted everywhere, enforced by tests, a script and a commit hook.

## 6. How I controlled the AI's output

Spec before code; tests before implementation for logic with clear contracts (parser, URL policy, file names, retry rules, store); every external behaviour proven against the real service at least once; an independent reviewer; mechanical gates (hooks) that cannot be argued with; and a written list of what is *not* verified (`verification.md`: keyboard and screen-reader checks by a person, colour contrast).

# Mars Rover Photo Downloader Constitution

This constitution is the highest-level **engineering governance** for the project. It states *how*
work is done, not *what* is built. Business requirements live in
`requirements/enterprise-requirements.md` and flow into `specs/` via Spec Kit. Where this
document and a spec conflict on engineering practice, this document wins; where they conflict on
business behavior, the approved spec wins and the conflict MUST be reported.

## Core Principles

### I. Specification-First & Traceability (NON-NEGOTIABLE)
Application code MUST NOT be written before an approved spec, plan, and task list exist for it.
Work follows: Requirements → Constitution → Specify → Clarify → Plan → Checklist → Tasks →
Analyze → Implement → Converge. Every task, code unit, and test MUST trace to a requirement ID
and acceptance criterion (`Requirement → Spec → Plan → Task → Code → Test → Acceptance`).
Uncertainty or conflicting requirements MUST stop work and be recorded as a question or decision;
they MUST NOT be resolved by silent guessing. Rationale: prevents invented requirements and
untraceable code.

### II. Simplicity & Fit-for-Purpose Architecture
The application is small. Use the simplest architecture that meets the requirements: a layered
.NET 8 solution (Domain, Application, Infrastructure, Api host) plus an Angular SPA. Microservices,
message brokers, databases, Kubernetes, caches, or other infrastructure MUST NOT be introduced
without a documented requirement that justifies them. Abstractions MUST earn their place (a real
seam for I/O, testing, or a second implementation). YAGNI applies.

### III. Clean Architecture, SOLID & Separation of Concerns
Dependencies point inward: Domain has no dependencies; Application depends on Domain and defines
ports (interfaces); Infrastructure implements ports; hosts compose them. Parsing, validation, API
access, storage, orchestration, and presentation MUST live in separate components with a single
responsibility. Constructor dependency injection is mandatory; no static mutable state, service
locators, or `new` of infrastructure inside business logic.

### IV. Secure Configuration & Secrets (NON-NEGOTIABLE)
Secrets (including the NASA API key) MUST NOT be hardcoded, committed, logged, returned in API
responses, or placed in Angular code. They are supplied via environment variables, user-secrets,
or a secret store, bound with `IOptions<T>` and validated at startup with actionable errors.
`.gitignore` MUST exclude local secret/config files. Any URL containing a key MUST be redacted
before logging.

### V. Input Validation & Untrusted Data
All user input, file content, and external API responses are untrusted. They MUST be validated
at the boundary. Paths and file names MUST be derived from validated values and sanitized (no
traversal). Download URLs MUST be restricted to allowed schemes and hosts, with size limits.
Parsing MUST use explicit formats and invariant culture; invalid input yields typed errors, not
exceptions used for control flow.

### VI. Async Correctness
All I/O MUST be asynchronous end-to-end. `.Result`, `.Wait()`, and `async void` are forbidden
(except event handlers). `CancellationToken` MUST be accepted and propagated by every public
async method that performs I/O. Concurrency MUST be bounded. `HttpClient` MUST be obtained via
`IHttpClientFactory`; streams MUST be disposed and large payloads streamed, not buffered.

### VII. External API Resilience
Calls to external systems MUST use timeouts, bounded retries with exponential backoff and jitter
for transient faults only (429, 5xx, timeouts, network), honor `Retry-After`, and never retry
permanent failures (400/403/404). Failures MUST be isolated per item so one failure never aborts
a batch. Rate-limit headers SHOULD be observed. Tests MUST NOT call live external services by
default.

### VIII. Idempotency & Data Integrity
Repeating an operation MUST NOT duplicate work or corrupt state. Files are written to a temporary
name and atomically moved into place; partial files MUST NOT be mistaken for complete ones.
Existing valid data MUST NOT be overwritten or deleted without explicit requirement.

### IX. Error Handling & Observability
Errors MUST be categorized with stable codes, reported clearly to the user, and never swallowed.
Exceptions are for exceptional conditions; expected failures use result types. Structured logging
via `ILogger` with appropriate levels and context (date, photo id, attempt) is required. Logs
MUST NOT contain secrets or sensitive data. A single global handler converts truly unexpected
errors to a logged failure and non-zero exit/`ProblemDetails`.

### X. Testing (NON-NEGOTIABLE)
Behavior MUST be verified by automated tests, not merely compilation. Unit tests cover parsing,
mapping, storage, and orchestration; integration tests cover the Web API with stubbed external
HTTP; Angular components have tests for success, validation error, API error, partial failure,
and empty states. Each acceptance criterion MUST have at least one test or documented manual
verification. Failing tests MUST NOT be bypassed, skipped, or weakened to pass. Prefer
tests-first for logic with clear contracts (parsing, mapping, idempotency).

### XI. AI-Generated Code Is Untrusted Until Verified
AI output MUST be reviewed for correctness, security, async/cancellation, resource disposal,
error handling, and requirement fit before acceptance. Compiling, "looks clean", or the model's
own assertion of correctness is not evidence. Defects found in AI output and their fixes MUST be
recorded in `AI_NOTES.md`. Official Microsoft .NET Agent Skills guide framework practice;
project skills and this constitution take precedence over generic preferences.

### XII. Code Review & Quality Gates
Every phase of implementation ends with build, test, review, fix, re-test. Reviews classify
findings CRITICAL / HIGH / MEDIUM / LOW / INFO; CRITICAL and HIGH findings MUST be resolved
before completion. Nullable reference types are enabled; compiler warnings are treated as errors
for new code; formatting is enforced by `.editorconfig`.

### XIII. Maintainability & Documentation
Names are intention-revealing; methods are small and cohesive; no magic strings for codes or
config keys. Public abstractions have XML docs. `README.md` and `AI_NOTES.md` are required
deliverables and MUST be accurate to the code. Significant decisions are recorded in the plan
(or an ADR) with rationale.

### XIV. Incremental Git History
Commits MUST be small, single-purpose, and descriptively named (imperative mood), made as work
completes — never one monolithic commit. Commits MUST NOT include secrets, build output, or
downloaded photos. Commit messages carry the required attribution trailer.

### XV. Performance & Accessibility
Streaming I/O, bounded concurrency, and no unnecessary allocations on hot paths. The Angular UI
MUST use semantic markup, alt text for images, keyboard operability, visible focus, and clear
loading/empty/error states; UI is responsive. Lazy-load thumbnails.

## Technology & Constraint Standards

- Backend: C# on .NET 8 (`net8.0`), ASP.NET Core Web API. The machine also has newer SDKs; the
  solution MUST pin .NET 8 via `global.json`/`TargetFramework`.
- Frontend: Angular (current LTS), TypeScript strict mode, standalone components.
- Storage: local file system for baseline scope; no database.
- External integration: NASA Mars Rover Photos API behind an application-layer port.
- Testing: xUnit for .NET with a mocked `HttpMessageHandler`/`WebApplicationFactory`; the
  Angular default test runner for the SPA.
- Dependencies MUST be minimal, pinned, and scanned (`dotnet list package --vulnerable`,
  `npm audit`) before completion.

## Development Workflow & Quality Gates

**Roles** (Claude agents): Business Analyst (never invents requirements), Solution Architect
(simplest viable design), Senior .NET Developer (implements approved tasks only), QA Engineer
(verifies behavior), Code Reviewer (classifies findings).

**Skills**: Microsoft .NET Agent Skills (`dotnet/skills`) are used for framework-specific
expertise, selected per task. Project skills hold project-specific conventions and MUST NOT
duplicate Microsoft's guidance.

**Mechanical gates (hooks)** enforce, without making business decisions:
1. *Pre-implementation*: constitution, spec, plan, and tasks exist; the task is authorized.
2. *Post-implementation*: build, tests, format/analysis pass.
3. *Pre-commit*: no secrets/credentials, no debug leftovers, build and tests green, no
   unintended files.
4. *Phase end*: acceptance criteria and traceability verified.
Hooks MUST fail safe and state clearly why they stopped.

**Analysis gate**: `/speckit-analyze` MUST be sufficiently clean before `/speckit-implement`.
Issues are fixed in the artifact that owns them, then analysis is rerun. After implementation,
`/speckit-converge` loops with `/speckit-implement` until convergence is reported.

**Definition of Done**: requirements traceable; build and tests pass; acceptance criteria
verified; security and code review pass; no secrets committed; README and `AI_NOTES.md`
present; meaningful commit history; converge reports convergence.

## Governance

This constitution supersedes other engineering practices. Amendments MUST be proposed with
rationale, recorded via `/speckit-constitution`, version-bumped per semantic versioning (MAJOR:
incompatible principle removal/redefinition; MINOR: new or materially expanded principle; PATCH:
clarification), and propagated to affected plan/task artifacts. All reviews and `/speckit-analyze`
runs MUST verify compliance; constitution conflicts are CRITICAL findings. Deviations require a
documented justification in the plan's Complexity Tracking section. Runtime guidance for agents
lives in `.claude/skills/` and `AI_NOTES.md`.

**Version**: 1.0.0 | **Ratified**: 2026-10-06 | **Last Amended**: 2026-10-06

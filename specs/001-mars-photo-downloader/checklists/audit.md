# Pre-Implementation Audit Checklist: Mars Rover Photo Downloader

**Purpose**: Validate the quality of the requirements, plan and contracts (completeness, clarity, consistency, measurability, coverage) before implementation starts
**Created**: 2026-10-06
**Feature**: [spec.md](../spec.md) · [plan.md](../plan.md) · [contracts/](../contracts/)
**Depth / audience**: Formal pre-implementation gate, for the reviewer. Whole-feature scope.

**Note**: This checklist tests whether the *requirements are well written*, not whether code works. `[x]` means the reviewer judged the requirements-quality criterion satisfied; it does not mean implementation is done. Generated items start unchecked; `/speckit-implement` reads but never changes these markers. REQ = `requirements/enterprise-requirements.md`.

## Requirement Completeness

- [ ] CHK001 - Is every functional area in the brief (input, parse, call, download, store, output) covered by at least one numbered spec requirement? [Completeness, Spec §FR-001–FR-020]
- [ ] CHK002 - Are the mandatory deliverables (README, AI_NOTES.md, small commits) expressed as requirements with verifiable content, not just mentioned? [Completeness, Spec §FR-027]
- [ ] CHK003 - Are requirements defined for what the system does when the same date list contains more than the supported number of lines or very long lines? [Gap, Contract §openapi RunRequest]
- [ ] CHK004 - Is the maximum supported input size (lines, file size) specified for the file-based input as well as the API input? [Gap, Spec §FR-001]
- [ ] CHK005 - Are requirements stated for retention, cleanup or growth of the `photos/` folder, or is "never delete" explicitly the intended policy? [Gap, Spec §FR-016]
- [ ] CHK006 - Is the behavior when two concurrent runs target the same photos folder (e.g. two API calls) specified or explicitly excluded? [Gap, Plan §D-11]
- [ ] CHK007 - Are requirements defined for how a run is started and cancelled from the visual page, including what the page shows after cancellation? [Gap, Spec §US6]
- [ ] CHK008 - Is the set of recognized date formats listed exhaustively and consistently across spec, REQ and research? [Completeness, Spec §FR-003, Plan §R-5]

## Requirement Clarity

- [ ] CHK009 - Is "first N photos" defined deterministically (ordering rule) in the spec itself, not only in the plan? [Clarity, Spec §FR-008, Plan §D-6]
- [ ] CHK010 - Is "photos of the Mars day that corresponds to the date" defined precisely enough (reference time of day, rounding rule) to give one answer per date? [Clarity, Spec §FR-011, Plan §R-3]
- [ ] CHK011 - Is the "mission range" for each rover defined, including the end date for rovers whose mission ended and the source of those dates? [Clarity, Spec §FR-007, Plan §R-4]
- [ ] CHK012 - Is "temporary failure" vs "permanent failure" defined by an explicit list of conditions in the spec? [Clarity, Spec §FR-012]
- [ ] CHK013 - Is "approved NASA hosts" enumerated in a requirement, or only in the plan? [Clarity, Spec §FR-018, Plan §D-7]
- [ ] CHK014 - Is "complete, non-empty file" defined clearly enough to distinguish a valid photo from a truncated one? [Clarity, Spec §FR-015]
- [ ] CHK015 - Are "clear message" and "plain-language reason" measurable (e.g. cause + remedy present)? [Ambiguity, Spec §FR-002, §FR-021]
- [ ] CHK016 - Is the meaning of "downloaded", "skipped" and "failed" counts unambiguous when a photo was skipped because it existed? [Clarity, Spec §FR-019]

## Requirement Consistency

- [ ] CHK017 - Does the spec's "refuse to run without a key" (FR-025) align with the clarification and REQ §FR-6.4, which previously allowed an opt-in demo-key fallback? [Conflict, Spec §FR-025, REQ §FR-6.4]
- [ ] CHK018 - Do the spec and REQ agree on out-of-range handling (REQ FR-2.9 amended) and on D-1 (REQ FR-3.13 amended) with no remaining contradictory wording elsewhere in REQ? [Consistency, REQ §FR-2.9, §FR-3.13, §22]
- [ ] CHK019 - Do the exit-code requirements agree between spec FR-022, plan D-12 and the CLI contract (codes 0/1/2/130)? [Consistency, Spec §FR-022, Contract §cli]
- [ ] CHK020 - Is the status vocabulary (`Success`, `NoPhotos`, `OutsideMissionRange`, ...) consistent between the data model, OpenAPI and CLI contract? [Consistency, Plan §data-model]
- [ ] CHK021 - Are the "3–5 photos" requirement and the "fewer photos available" scenario reconciled (is a result with 1–2 photos a success or a shortfall)? [Ambiguity, Spec §US1-3, §FR-008]
- [ ] CHK022 - Does the REQ unit-test table still list expected outcomes that contradict clarified behaviour (e.g. rover/date assumptions)? [Consistency, REQ §15.1]

## Acceptance Criteria Quality

- [ ] CHK023 - Does every functional requirement map to at least one measurable acceptance criterion or success criterion? [Traceability, Spec §SC-001–SC-010]
- [ ] CHK024 - Is "between 3 and 5 photos" in SC-002 conditional on source availability in a way that can be objectively evaluated with fixtures? [Measurability, Spec §SC-002]
- [ ] CHK025 - Can SC-008 (under 30 seconds) be objectively measured given the fallback needs several requests per date and retries may add delay? [Measurability, Spec §SC-008]
- [ ] CHK026 - Is SC-007 ("zero occurrences of the credential") defined over a stated set of artifacts and a stated search method? [Measurability, Spec §SC-007]
- [ ] CHK027 - Is the "at least 8 small commits" criterion defined well enough to check (what counts as small)? [Measurability, Spec §SC-009]
- [ ] CHK028 - Are acceptance criteria written for the visual results page beyond "usable by keyboard", with a measurable accessibility target? [Gap, Spec §FR-023]

## Scenario & Edge Case Coverage

- [ ] CHK029 - Are alternate-flow requirements defined when the primary succeeds for some dates and is unavailable for others within one run? [Coverage, Spec §US5]
- [ ] CHK030 - Are recovery requirements defined for a run interrupted mid-way, including leftover partial files from earlier runs? [Coverage, Spec §US3-2]
- [ ] CHK031 - Is behavior specified when the fallback returns photos but the chosen image address is rejected by the host policy? [Coverage, Spec §FR-018]
- [ ] CHK032 - Is behavior specified when the primary returns photos whose address uses a host outside the allow-list? [Coverage, Gap, Spec §FR-018]
- [ ] CHK033 - Are requirements defined for photos with identical file names from different providers for the same date (provider switch between runs)? [Gap, Spec §FR-014–FR-015]
- [ ] CHK034 - Are time-zone requirements stated for "today" (future-date check) and for the date-to-Mars-day conversion? [Gap, Spec §FR-007]
- [ ] CHK035 - Is the behavior for a date list with only invalid dates (no lookups at all) defined, including output and exit status? [Coverage, Spec §FR-022]
- [ ] CHK036 - Are zero-byte and non-image responses (HTML error page served with 200) covered by an explicit requirement? [Coverage, Spec §FR-017]

## Non-Functional: Security

- [ ] CHK037 - Are secret-handling requirements defined for every channel (source, history, logs, console, API, UI, error messages, URLs)? [Completeness, Spec §FR-024, §FR-026]
- [ ] CHK038 - Is the requirement to redact the key from request addresses in logs specific enough to cover third-party/library logging? [Clarity, Spec §FR-026]
- [ ] CHK039 - Are requirements defined for what happens to the key already present in the git history of earlier commits, or is "no history exposure" verified by a stated check? [Gap, Spec §SC-007]
- [ ] CHK040 - Are cross-origin and static-file exposure requirements stated at the requirement level (what may be reachable from the browser)? [Gap, Plan §D-13]
- [ ] CHK041 - Are untrusted-input requirements defined for API-supplied date lines (size, characters) in addition to file input? [Coverage, Spec §FR-005]

## Non-Functional: Resilience, Error Handling, Observability

- [ ] CHK042 - Are retry limits, total time budget per date and backoff bounds quantified in requirements rather than left as plan defaults? [Clarity, Spec §FR-012–FR-013]
- [ ] CHK043 - Are rate-limit awareness requirements quantified (what remaining-quota level triggers a warning)? [Ambiguity, REQ §FR-3.9]
- [ ] CHK044 - Is the complete error-category list stable and referenced identically by spec, REQ §9.2 and the data model? [Consistency, Spec §FR-021]
- [ ] CHK045 - Are logging requirements defined (events, levels, correlation context) well enough to be testable without prescribing a library? [Measurability, Spec §FR-026]
- [ ] CHK046 - Is there a requirement for how fallback usage and reasons are surfaced in logs as well as in the summary? [Gap, Spec §FR-010]

## Non-Functional: Accessibility, Maintainability, Configuration

- [ ] CHK047 - Are accessibility requirements for the page specified for status (non-colour cues), focus order, image alternatives and live status updates? [Completeness, Spec §FR-023, §US6-4]
- [ ] CHK048 - Are page states (loading, empty, error, partial failure) each defined with required content? [Completeness, Spec §US6]
- [ ] CHK049 - Are configuration items, defaults, valid ranges and precedence listed in one authoritative place and consistent with the spec? [Consistency, Spec §FR-024, Plan §data-model]
- [ ] CHK050 - Are maintainability requirements (layering, test seams, code-quality gates) stated as testable constraints rather than aspirations? [Measurability, Constitution §III, §XII]
- [ ] CHK051 - Is the requirement for tests-first versus tests-after stated unambiguously for each area? [Ambiguity, Constitution §X]

## Dependencies, Assumptions & Risks

- [ ] CHK052 - Is the dependency on an undocumented fallback source recorded with a defined response if its structure changes? [Dependency, Spec §Assumptions, REQ §R-2]
- [ ] CHK053 - Is the assumption that the three sample dates have photos in the fallback validated and recorded with evidence and date? [Assumption, REQ §22, Plan §R-3]
- [ ] CHK054 - Are the Opportunity/Spirit behaviours (primary only) reflected in an acceptance criterion rather than only in assumptions? [Gap, Spec §Edge Cases]
- [ ] CHK055 - Is the fallback source's terms-of-use or attribution expectation addressed? [Gap, REQ §NFR-12]

## AI-Generated Code Governance

- [ ] CHK056 - Are verification requirements for AI-generated code specific (which checks, which evidence) rather than "review it"? [Clarity, Constitution §XI]
- [ ] CHK057 - Does the spec/plan define what must be recorded in AI_NOTES.md for each AI defect found (so it can be audited)? [Completeness, Spec §FR-027]
- [ ] CHK058 - Are the mechanical gates (hooks) specified with the exact conditions that block work, including what is *not* blocked? [Clarity, Constitution §Workflow]

## Traceability

- [ ] CHK059 - Is a requirement ID scheme in place and are spec IDs (FR-001…) distinguishable from REQ IDs (FR-1.x) so citations are unambiguous? [Traceability, Spec §header]
- [ ] CHK060 - Will every requirement, task and test carry a traceable ID, and is a traceability matrix an explicit deliverable? [Traceability, Plan §Constitution Check I]

## Notes

- Items CHK017, CHK018, CHK021, CHK022 flag possible cross-document conflicts that `/speckit-analyze` should also examine.
- Items marked `[Gap]` are candidates for spec/plan edits or explicit "out of scope" statements before implementation.
- Reviewer: mark `[x]` only when the requirement text itself satisfies the question.

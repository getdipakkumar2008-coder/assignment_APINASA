---
name: requirements-traceability
description: How to keep Requirement -> Spec -> Plan -> Task -> Code -> Test -> Acceptance traceability in this repo.
---
- Every spec requirement cites a source ID from `requirements/enterprise-requirements.md` (FR-/NFR-/SEC-/AC-).
- Every task in tasks.md lists the requirement IDs it satisfies; tests name the AC they verify (xUnit `[Trait("AC","AC-5")]`).
- Maintain `specs/<feature>/traceability.md` as a matrix; orphan tasks or untested requirements are findings.
- Never change business behavior silently: change the requirements doc or log an open question.

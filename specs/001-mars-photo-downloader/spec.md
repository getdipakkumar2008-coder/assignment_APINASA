# Feature Specification: Mars Rover Photo Downloader

**Feature Branch**: `001-mars-photo-downloader`

**Created**: 2026-10-06

**Status**: Draft

**Input**: User description: "Build the Mars Rover Photo Downloader described in `requirements/enterprise-requirements.md` (authoritative business source, including §22 verification findings and the approved provider-fallback decision OQ-10)."

**Source of truth**: `requirements/enterprise-requirements.md` (referred to as "REQ"). Bracketed IDs such as `[FR-2.5]` trace each statement to REQ.

## Clarifications

### Session 2026-10-06

- Q: Is the visual results page in scope? → A: **Yes**, it is a delivered feature (OQ-1).
- Q: Missing credential: refuse to run or fall back to the shared demo key? → A: The operator supplied a project-specific NASA key to be used through external settings; the shared demo key is **not** used. If no key is configured the tool refuses to run (OQ-6). *Interpretation: the answer provided a key rather than choosing a policy; "refuse, never demo key" is the reading that is consistent with it.* The key is kept only in a git-ignored local settings file.
- Q: Should a run with partial failures still end with a success exit status? → A: **Not answered; default retained** (OQ-7): a run that completes and reports per-date problems ends with success status; only fatal conditions (missing key, unreadable input file, unwritable output) end with failure status.
- Q: Dates outside the rover's mission range: flag only, or skip the lookup? → A: Skip the photo lookup and report them as "outside mission range"; every date inside the mission range is looked up normally (FR-2.9). *Interpretation of "ignore API call but any valid date run api call".*

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Turn a list of dates into saved rover photos (Priority: P1)

An operator maintains a plain-text list of dates, written in inconsistent human formats. They run the tool once and it saves a handful of Mars rover photographs for each valid date into a folder named for that date, then shows a summary.
[BR-1, BR-3, BR-4, FR-1, FR-2, FR-3, FR-4, FR-5]

**Why this priority**: This is the whole purpose of the product. Without it nothing else has value.

**Independent Test**: Provide the four sample dates (`02/27/17`, `June 2, 2018`, `Jul-13-2016`, `April 31, 2018`), run once, and confirm photos are stored for the three real dates and the summary reports all four lines.

**Acceptance Scenarios**:

1. **Given** the sample date list and a reachable photo source, **When** the operator runs the tool, **Then** photos are saved in `photos/2017-02-27/`, `photos/2018-06-02/` and `photos/2016-07-13/`, with between 3 and 5 photos per date. [AC-2, AC-3, AC-4, FR-3.3, FR-4.1]
2. **Given** the same list, **When** the run finishes, **Then** no folder exists for the invalid date `April 31, 2018`. [AC-4]
3. **Given** a date for which the source holds fewer than the requested number of photos, **When** the tool runs, **Then** it saves what exists and reports the shortfall without error. [FR-3.6, A-7]

---

### User Story 2 - Understand exactly what happened (Priority: P1)

After a run, the operator reads a clear summary showing, for every input line, the date, how many photos were downloaded, and any validation or retrieval errors, plus overall totals.
[BR-4, FR-5.1–FR-5.3, AC-7]

**Why this priority**: A silent or ambiguous result makes the tool untrustworthy; the brief explicitly requires this output.

**Independent Test**: Run with a mix of valid, invalid and unreachable dates and verify that each input line appears once with its own outcome and the totals add up.

**Acceptance Scenarios**:

1. **Given** a run with valid and invalid dates, **When** the summary is shown, **Then** each line lists the original text, the normalized date (if valid), counts of downloaded / skipped / failed photos, and any error with a plain-language reason. [FR-5.1, FR-5.3]
2. **Given** the invalid date `April 31, 2018`, **When** the summary is shown, **Then** it states the date does not exist (April has 30 days) rather than a generic failure. [FR-2.5, NFR-11]
3. **Given** any run, **When** results are displayed, **Then** no secret credential appears anywhere in the output. [FR-5.7, SEC-1]

---

### User Story 3 - Re-run safely without repeated downloads (Priority: P1)

The operator can run the tool again, for example after a failure or after adding a date, and already-saved photos are not downloaded again and never end up damaged.
[BR-3, FR-4.3, FR-4.4, NFR-3, AC-5]

**Why this priority**: Idempotency saves bandwidth, respects the provider's limits and is an explicit acceptance criterion.

**Independent Test**: Complete a run, run again unchanged, and confirm all photos are reported as already present and the stored files are untouched.

**Acceptance Scenarios**:

1. **Given** a completed run, **When** the tool runs again with the same list, **Then** zero photos are downloaded and all are reported as skipped because they already exist. [AC-5]
2. **Given** a previous run interrupted mid-download, **When** the tool runs again, **Then** the incomplete photo is fetched again and no damaged photo is mistaken for a complete one. [FR-4.4]
3. **Given** the list is extended with one new date, **When** the tool runs, **Then** only the new date's photos are downloaded. [NFR-3]

---

### User Story 4 - Keep working when things go wrong (Priority: P1)

Bad dates, an unavailable photo source, rate limits, slow networks and individual failed downloads never crash the run or block the other dates.
[BR-2, FR-1.5, FR-2.10, FR-3.7, FR-3.8, FR-4.6, NFR-2, NFR-4, AC-6]

**Why this priority**: Real-world inputs and a third-party service are unreliable; graceful degradation is a core brief requirement. This matters in practice: the originally specified photo service is currently down. [F-2]

**Independent Test**: Simulate an unreachable source, a rate-limit response, a single failing photo and a malformed date file; verify the tool completes, reports each problem clearly and still processes everything else.

**Acceptance Scenarios**:

1. **Given** the date file is missing, empty or unreadable, **When** the tool runs, **Then** it reports a clear message and stops cleanly without a crash. [FR-1.5]
2. **Given** a temporary failure or rate-limit response from the photo source, **When** the tool retries and still fails, **Then** that date is reported with a clear error and the remaining dates are still processed. [FR-3.7, FR-3.8, AC-6]
3. **Given** one photo cannot be downloaded, **When** the run continues, **Then** the other photos and dates are saved and the failure is listed. [FR-4.6]
4. **Given** a request that can never succeed (for example an invalid credential), **When** the tool encounters it, **Then** it does not keep retrying and reports the cause. [FR-3.8]
5. **Given** the network drops mid-run, **When** the run ends, **Then** no corrupt or partial photo files remain as if complete. [AC-6, FR-4.4]

---

### User Story 5 - Keep getting photos while the original photo service is unavailable (Priority: P2)

Because the Mars Rover Photos service named in the brief is currently unavailable, the tool tries it first and, when it cannot serve a date, automatically obtains photos for that date from NASA's other public rover-image source, and tells the operator which source supplied each date.
[OQ-10 (approved), FR-3.11–FR-3.16, F-2, F-4, F-5]

**Why this priority**: The brief mandates the original source, so it stays the first choice. Without the fallback the tool delivers nothing today. It is P2 only because it extends the P1 flow.

**Independent Test**: With the original source unavailable, run the sample dates and verify photos for the three valid dates are still saved and the summary names the fallback source.

**Acceptance Scenarios**:

1. **Given** the original source returns "not found" or an error page, **When** a valid date is processed, **Then** the tool falls back automatically and reports the reason and the source used. [FR-3.14, FR-3.15]
2. **Given** both sources fail for a date, **When** the run ends, **Then** that date shows a clear "source unavailable" error and the other dates are unaffected. [FR-3.14]
3. **Given** the fallback source is used, **When** photos are selected, **Then** they are the photos of the Mars day that corresponds to the requested Earth date, even if some were captured a few hours before midnight of that date. **[AMENDMENT D-1, APPROVED 2026-10-06]** [FR-3.13]

---

### User Story 6 - Browse results visually (Priority: P2, in scope)

An operator opens a simple visual page, triggers a run and sees the per-date results, errors and thumbnails of the saved photos.
[BR-4, FR-5.4, FR-5.5, FR-7, AC-12]

**Why this priority**: It improves usability and is required if a UI is delivered, but the console summary alone satisfies the core brief.

**Independent Test**: Open the page after a run and verify every date shows status, counts, errors and thumbnails, including states for loading, empty and failure.

**Acceptance Scenarios**:

1. **Given** a completed run, **When** the page is opened, **Then** each date shows status, counts, any errors and thumbnails of saved photos. [FR-7.3, FR-7.4]
2. **Given** a run with partial failures, **When** results are displayed, **Then** successful and failed dates are visually distinguishable. [FR-7.3]
3. **Given** a date with no photos, **When** displayed, **Then** the page shows a clear empty state, not a blank area or an error. [FR-3.6, FR-7.5]
4. **Given** a keyboard-only or screen-reader user, **When** they use the page, **Then** all results and images are operable and described. [FR-7.6, NFR-11]

---

### User Story 7 - Configure it without touching code or exposing secrets (Priority: P2)

The operator supplies the NASA credential and options (source choice, rover, photos per date, input file, output folder) through external settings, and the credential is never stored in the code base or shown.
[FR-6, SEC-1, SEC-2, SEC-10, AC-8]

**Why this priority**: Required by the brief ("do not hardcode API keys") but supporting rather than the main journey.

**Independent Test**: Search the repository and a full run's output for the credential: it must not appear. Remove the credential and confirm a clear, actionable message.

**Acceptance Scenarios**:

1. **Given** no credential is configured, **When** the tool starts, **Then** it stops with a message explaining how to supply one. [FR-6.4, A-8]
2. **Given** a configured credential, **When** the repository, logs, summaries and visual page are inspected, **Then** the credential does not appear. [SEC-1, FR-5.7, NFR-9]

### Edge Cases

- **Calendar-impossible dates** such as April 31, or February 29 in a non-leap year, are reported as invalid, never rolled forward to another date. [FR-2.5]
- **Two-digit years** (`17`) resolve to the same year on every machine regardless of regional settings. [FR-2.4, NFR-1]
- **Text that matches no known format**, blank lines and surrounding spaces: unrecognized text is reported with the original wording, blanks are ignored, spaces are trimmed. [FR-1.4, FR-2.6]
- **Duplicate dates** written differently (e.g. `02/27/17` and `2017-02-27`): processed once and the duplication noted. [FR-2.8]
- **Dates before the rover landed or in the future**: no lookup is made; reported as outside the mission range with zero photos. [FR-2.9]
- **A date with no photos**: a valid outcome, reported as zero photos rather than an error. [FR-3.6, A-7]
- **Photo source returns a web page instead of data**, malformed data, or fields missing: classified as a bad response, never a crash. [FR-3.5, FR-3.15]
- **Photo already present but empty (zero size)**: treated as incomplete and downloaded again. [FR-4.3]
- **Output folder unwritable or disk full**: reported clearly; other work unaffected where possible. [FR-4.9]
- **Photo address from an untrusted host or unsafe path text**: rejected, never written outside the photos folder. [SEC-3, SEC-4]
- **Rate limit nearly exhausted**: operator is warned. [FR-3.9]
- **Rover other than Curiosity**: the fallback source covers only Curiosity, so other rovers report as unavailable when the original source is down. [§22.2]
- **Operator cancels the run**: stops cleanly leaving no partial files mistaken for complete. [NFR-4, FR-4.4]

## Requirements *(mandatory)*

### Functional Requirements

**Date input and validation**
- **FR-001**: The system MUST read dates from a text file, one per line, ignoring blank lines and trimming whitespace, and the delivered file MUST contain exactly the four sample lines. [FR-1.1, FR-1.2, FR-1.4, DEL-2]
- **FR-002**: The system MUST report a missing, empty or unreadable file as a clear error without crashing. [FR-1.5]
- **FR-003**: The system MUST recognize at least these formats: numeric month/day/two-digit-year (`02/27/17`), full month name with day and year (`June 2, 2018`), and abbreviated month with day and year separated by hyphens (`Jul-13-2016`); further documented variants are permitted. [FR-2.1, FR-2.2]
- **FR-004**: The system MUST interpret slash-separated numeric dates as month-first and two-digit years as 20xx, identically on every machine. [A-1, A-2, FR-2.4, NFR-1]
- **FR-005**: The system MUST classify each line as valid, invalid (not a real calendar date) or unrecognized, preserving the original text and giving a plain-language reason, and MUST NOT stop processing other lines because of one bad line. [FR-2.5, FR-2.6, FR-2.10]
- **FR-006**: The system MUST normalize valid dates to year-month-day for retrieval and folder names, and MUST process duplicate dates once with a note. [FR-2.7, FR-2.8]
- **FR-007**: The system MUST NOT query any photo source for a valid calendar date that falls outside the rover's mission range (before landing or in the future); it MUST report that date as "outside mission range" with zero photos and no error, and MUST still query for every valid date inside the range. [FR-2.9, Clarification 2026-10-06]

**Photo retrieval**
- **FR-008**: For each valid date the system MUST retrieve the first N photos (default 5, minimum 3, configurable) taken on that Earth date for the configured rover (default Curiosity). [FR-3.1–FR-3.3, A-3, A-4]
- **FR-009**: The system MUST treat "no photos for this date" as a valid, non-error outcome. [FR-3.6]
- **FR-010**: The system MUST first use the Mars Rover Photos service as specified, and on failure automatically fall back to NASA's alternative public rover-image source, recording the reason and which source served each date. [OQ-10, FR-3.11–FR-3.14]
- **FR-011**: The fallback MUST convert the requested Earth date to the rover's Mars day and return the photos of that Mars day, which is the meaning the original service gives to an Earth date. **[AMENDMENT D-1, APPROVED 2026-10-06: replaces "keep only photos taken on the requested Earth date", which yields no photos for 2018-06-02]** [FR-3.13, F-4]
- **FR-012**: The system MUST categorize failures (bad request, not found, invalid credential, rate limited, server error, timeout, network failure, malformed or non-data response), retry only temporary ones with increasing waits and respect any provider "retry after" guidance, and never retry permanent failures. [FR-3.7, FR-3.8, FR-3.15]
- **FR-013**: The system MUST apply timeouts to all external requests, keep request pacing within provider limits and support operator cancellation. [FR-3.10, NFR-4]

**Storage**
- **FR-014**: The system MUST save each photo under `photos/<year-month-day>/` using a unique, predictable, filesystem-safe name. [FR-4.1, FR-4.2]
- **FR-015**: The system MUST NOT download a photo that already exists as a complete, non-empty file, and MUST report it as skipped. [FR-4.3, AC-5]
- **FR-016**: The system MUST ensure an interrupted download never leaves a file that looks complete, and MUST NOT overwrite or delete existing valid files. [FR-4.4, FR-4.10]
- **FR-017**: The system MUST isolate failures so one failed photo or date never stops the others, and MUST validate each download is a non-empty image before keeping it. [FR-4.6, FR-4.8, NFR-2]
- **FR-018**: The system MUST only accept photos from approved NASA hosts over web addresses it considers safe, cap photo size, and derive folder and file names only from validated values. [SEC-3, SEC-4, FR-3.16, SEC-6]

**Output**
- **FR-019**: The system MUST present, per input line: the original text, the normalized date, the supplying source, counts of downloaded / skipped / failed photos, and any validation or retrieval errors; plus overall totals. [FR-5.1–FR-5.3]
- **FR-020**: The system MUST provide this result as an on-screen summary (guaranteed minimum) and MUST also provide it in a machine-readable form for other programs and for the visual page. [FR-5.2, FR-5.4, Audit I2]
- **FR-021**: The system MUST use stable error categories with clear remedies in plain language. [§9.2, NFR-11]
- **FR-022**: The system MUST end with success status when the run completed, even if individual dates reported problems, and with failure status only for fatal conditions (no credential, unreadable input file, unwritable output location, unexpected crash). [FR-5.6, OQ-7]
- **FR-023**: The system MUST offer a visual results page showing per-date status, counts, errors and thumbnails, with loading, empty and error states, usable by keyboard and assistive technology. [FR-5.5, FR-7.1–FR-7.6]

**Configuration and security**
- **FR-024**: The system MUST take the NASA credential, source, rover, photo count, input file, output folder, timeouts, retries and concurrency from external settings; the credential MUST NOT appear in code, committed files, logs, output or the visual page. [FR-6.1–FR-6.4, SEC-1, SEC-2, SEC-10]
- **FR-025**: The system MUST refuse to start without a credential and explain how to supply one; it MUST NOT substitute a shared demo credential. [FR-6.4, OQ-6, Clarification 2026-10-06]
- **FR-026**: The system MUST record progress and problems in a log containing context (date, photo, attempt) but never secrets, including the credential inside request addresses. [NFR-9, SEC-1, §11.2]

**Delivery**
- **FR-027**: The project MUST ship a README (how to run, assumptions), an `AI_NOTES.md` (tools, best prompts, one AI mistake and its fix, significant changes), and a history of multiple small commits. [DEL-3, DEL-4, DEL-5, AC-9, AC-10]
- **FR-028**: The project SHOULD include automated tests for date handling, source response handling, storage, processing and failure cases, and MAY include a container recipe. [§15, DEL-6, DEL-7, NFR-15]

### Key Entities

- **Date Input Line**: one line from the date file: original text, line number.
- **Parsed Date**: result of interpreting a line: valid / invalid / unrecognized, normalized date, reason.
- **Rover Photo**: one image from a source: identifier, Mars day, camera, address, Earth date taken, rover.
- **Photo Outcome**: result for one photo: downloaded / skipped / failed, stored location, reason.
- **Date Result**: everything for one input line: date, supplying source, available count, photo outcomes, errors, and a status (including `Cancelled` for dates not processed because the run was stopped).
- **Run Summary**: all date results plus totals, rover, time and duration.
- **Error Category**: stable named reasons (invalid date, unrecognized, no photos, rate limited, source unavailable, bad response, download failed, storage error).
- **Photo Source**: a place photos can be obtained from; first choice and fallback.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Of the four sample lines, exactly three are recognized as valid dates (2017-02-27, 2018-06-02, 2016-07-13) and one is reported invalid with a clear reason, with zero crashes. [AC-2]
- **SC-002**: For each of the three valid sample dates, between 3 and 5 photos are saved when the source has them, in the folder for that date. [AC-3, AC-4]
- **SC-003**: A second run over unchanged inputs downloads 0 photos and leaves 100% of stored files unmodified. [AC-5]
- **SC-004**: With the original photo service unavailable, the three valid sample dates still yield photos through the fallback, with the supplying source named per date. [OQ-10]
- **SC-005**: Under simulated failures (unreachable source, rate limit, one bad download, missing file, mid-run network loss) the run always finishes with a report, 100% of unaffected dates are still processed, and 0 incomplete photos are left looking complete. [AC-6, NFR-2]
- **SC-006**: Every input line appears exactly once in the summary with its date, downloaded count and errors; totals equal the sum of lines. [AC-7]
- **SC-007**: A search of the repository history, run output, logs and visual page finds 0 occurrences of the credential. [AC-8]
- **SC-008**: A typical run (4 dates × 5 photos) completes in under 30 seconds on a normal connection when the sources respond normally. [NFR-5]
- **SC-009**: A new reader can build and run the project from the README alone, and finds the AI notes and a history of at least 8 small commits. [AC-1, AC-9, AC-10]
- **SC-010**: Each acceptance criterion AC-1..AC-11 has at least one automated test or documented manual verification. [§14, §15]

### Audit-Resolved Definitions (from `checklists/audit.md`, 2026-10-06)

Precision added to close audit gaps; none change approved business behavior. Reviewer to confirm.

- **FR-029 (limits)**: Input files MUST be limited to 1000 non-blank lines and 200 characters per line; run requests supplied directly MUST be limited to 100 lines. Excess lines are reported as unrecognized with a limit message, not silently dropped. [CHK003, CHK004]
- **FR-030 (ordering)**: "First N photos" means photos ordered ascending by the source's photo identifier: numerically when every identifier is a whole number, otherwise in plain character order; fewer than N available is still a success, reporting how many were available. [CHK009, CHK021]
- **FR-031 (Mars day)**: The Mars day for an Earth date is computed from noon UTC of that date relative to the rover's landing instant, using the Mars day length of 88 775.244 seconds, rounded down. "Today" and all date comparisons use UTC. [CHK010, CHK034]
- **FR-032 (mission range)**: Curiosity: 2012-08-06 to today. Opportunity: 2004-01-25 to 2018-06-10. Spirit: 2004-01-04 to 2010-03-22. Dates outside these inclusive ranges are "outside mission range". [CHK011]
- **FR-033 (failure classes)**: *Temporary*: timeout, connection failure, HTTP 408, 429, and 5xx. *Permanent*: HTTP 400, 401, 403, 404 and non-data (web page) responses. Up to 3 retries, waits starting at 1 second and doubling up to 8 seconds with random jitter, within a total budget of 60 seconds per request. [CHK012, CHK042]
- **FR-034 (approved hosts)**: Only `mars.nasa.gov` and `mars.jpl.nasa.gov` over HTTPS are accepted for photo downloads, from either source; any other host is rejected with a download-rejected error. [CHK013, CHK032]
- **FR-035 (complete file)**: A stored photo is complete only if it was fully received, is an image type and is larger than zero bytes; anything else is discarded. [CHK014, CHK036]
- **FR-036 (counts)**: A photo already present counts as *skipped*, never *downloaded*; *downloaded* counts only newly stored photos in this run. [CHK016]
- **FR-037 (concurrent runs)**: Only one run may be active at a time; a second start request while one is running MUST be refused with a clear "run in progress" message. [CHK006]
- **FR-038 (retention)**: Stored photos are never deleted or aged out by the tool; clearing them is the operator's responsibility. [CHK005]
- **FR-039 (all invalid)**: A list with no valid dates ends normally with a full summary, no lookups and success status. [CHK035]
- **FR-040 (observability)**: Each fallback decision MUST be logged as a warning with the date and reason; the rate-limit warning triggers when fewer than 100 requests remain in the provider's quota. [CHK043, CHK046]
- **FR-041 (accessibility target)**: The visual page MUST meet WCAG 2.1 level AA for the elements it contains (status shown with text not only colour, visible focus, alternative text for photos, live announcement of run status). [CHK028, CHK047]
- **FR-042 (cancel)**: The operator MUST be able to stop a run from the visual page; the page then shows results so far, with dates not yet processed marked `Cancelled`. [CHK007]
- **FR-043 (attribution)**: The README MUST credit NASA/JPL-Caltech as the image source. [CHK055]
- **Terminology**: "photo source" means a place photos can be obtained from (the Mars Rover Photos service or NASA's raw-image feed); the implementation calls these "providers".
- **Same-name photos across sources**: a photo is skipped when a file with its final name exists; switching sources between runs may therefore store a photo under a second name. Accepted limitation. [CHK033]

## Assumptions

Adopted from REQ §5.1 and §22; each can be revised through `/speckit-clarify`.

- Slash dates are month-first; `17` means 2017 [A-1, A-2]. Earth dates, not Mars days, are what operators supply [A-6].
- Curiosity is the default rover; Opportunity and Spirit are accepted by the original source only, and are reported unavailable when the fallback is used [A-3, §22.2].
- "First 3–5 photos" means a configurable count, default 5 [A-4, OQ-4].
- One date per line; comment lines are optional [A-5, FR-1.6].
- "No photos for a date" is not an error [A-7].
- A NASA credential is available to the operator; the project key allows about 2000 requests per hour [F-1]. The tool refuses to run without one and never uses a shared demo key [OQ-6].
- The operator has outbound internet access to NASA services [A-8].
- Partial failures end a run normally with errors reported; only fatal problems signal failure [OQ-7, retained default, unanswered].
- A console summary and machine-readable output are the baseline; the visual results page is confirmed in scope [OQ-1, OQ-5].
- Out of scope: image editing, user accounts, databases, cloud hosting, other NASA services, fetching beyond the first N photos [§4.2, OQ-9].
- Dependencies: NASA's original photo service (currently unavailable) and NASA's raw-image feed; the latter covers Curiosity only and exposes web addresses on NASA-controlled hosts [F-2, F-4].
- Risks accepted: the fallback source's structure is not a documented contract and may change [R-2, F-4]; image addresses may need a secure-connection upgrade [FR-3.16, R-6].

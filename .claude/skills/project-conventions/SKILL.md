---
name: project-conventions
description: Project-specific .NET/Angular conventions for the Mars Rover Photo Downloader (layout, config, naming, error codes). Use when writing or reviewing any code here.
---
- Solution layout: `src/MarsPhotos.{Domain,Application,Infrastructure,Api}`, `src/marsphotos-ui` (Angular), `tests/MarsPhotos.*.Tests`. Pin .NET 8 via `global.json`.
- Config: `NASA__ApiKey` from env/.env/user-secrets via `IOptions<NasaOptions>` with `ValidateOnStart`. Never in appsettings.json or Angular.
- Dates: `DateOnly.TryParseExact`, explicit format list, `CultureInfo.InvariantCulture`, results as typed `ParsedDate` (no exceptions for control flow).
- Error codes are constants (see requirements §9.2); no magic strings.
- Files: `photos/<yyyy-MM-dd>/<photoId>_<camera>.<ext>`, write to `.part` then atomic move, skip if exists and non-empty.
- Logging: `ILogger` structured; redact `api_key` query values.
- Generic .NET framework guidance comes from the Microsoft .NET skills; do not restate it here.

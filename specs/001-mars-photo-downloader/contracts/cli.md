# Contract: Console Runner

```text
dotnet run --project src/MarsPhotos.Cli -- [--file <path>] [--rover <name>] [--count <3-5>] [--out <dir>] [--json]
```
Run from the repository root. Key comes from `NASA__ApiKey` (environment or `.env`). CLI arguments override environment, which overrides appsettings.

## Output (default)
```text
Mars Rover Photo Downloader - rover: curiosity
Input            Date        Status               Source          New  Skip  Fail  Notes
02/27/17         2017-02-27  Success              RawImageFeed      5     0     0
...
April 31, 2018   -           InvalidDate          -                 0     0     0  DATE_INVALID: Day 31 does not exist in April 2018
Totals: 4 lines | 3 valid | 1 invalid | 0 outside range | 15 downloaded | 0 skipped | 0 failed
```
`--json` prints the `RunSummary` JSON (same schema as the API) to stdout; logs go to stderr.

## Exit codes
| Code | Meaning |
|---|---|
| 0 | Run completed (per-date problems are reported, not fatal) |
| 1 | Fatal: missing or invalid key, unreadable input file, unwritable output root, invalid arguments |
| 2 | Unexpected unhandled error |
| 130 | Cancelled (Ctrl+C) |

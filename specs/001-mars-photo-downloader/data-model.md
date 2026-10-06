# Data Model: Mars Rover Photo Downloader

All types are immutable records in `MarsPhotos.Domain` unless noted. No persistence; the only stored data are photo files.

## Entities

### DateInputLine
| Field | Type | Rules |
|---|---|---|
| LineNumber | int | 1-based; blank lines are skipped but numbering preserved |
| RawText | string | trimmed original text; max 200 chars (longer → `DATE_UNRECOGNIZED`) |

### ParsedDate
| Field | Type | Rules |
|---|---|---|
| Line | DateInputLine | |
| Status | `DateStatus` | `Valid`, `Invalid`, `Unrecognized` |
| Value | DateOnly? | set only when `Valid` |
| ErrorCode / Message | string? | `DATE_INVALID` / `DATE_UNRECOGNIZED`; plain-language reason |

### RoverPhoto
| Field | Type | Rules |
|---|---|---|
| Id | string | provider-unique; stem for the file name after sanitizing |
| Sol | int? | |
| Camera | string | instrument or camera name; may be `UNK` |
| ImageUrl | Uri | must pass `UrlPolicy` (https, allow-listed host) |
| EarthDate | DateOnly | requested date (the sol's assigned date) |
| Rover | string | |

### PhotoOutcome
`PhotoId`, `Status` (`Downloaded` / `Skipped` / `Failed`), `RelativePath?`, `ErrorCode?`, `Message?`, `Bytes?`.

### DateResult
| Field | Type | Rules |
|---|---|---|
| Input | string | original text |
| Date | DateOnly? | null if not valid |
| Status | `DateResultStatus` | `Success`, `NoPhotos`, `InvalidDate`, `UnrecognizedDate`, `OutsideMissionRange`, `DuplicateDate`, `SourceUnavailable`, `PartialFailure`, `Failed` |
| Provider | string? | `MarsRoverPhotos` or `RawImageFeed` (which source served it) |
| FallbackReason | string? | why the primary was bypassed |
| PhotosAvailable | int | count the provider returned for the date (before selecting N) |
| Photos | PhotoOutcome[] | |
| Errors | ErrorInfo[] | |
| Downloaded / Skipped / Failed | int | derived |

### RunSummary
`Rover`, `StartedAt`, `Duration`, `Totals` (lines, valid, invalid, outsideRange, downloaded, skipped, failed), `Results[]` (one per input line, input order).

### ErrorInfo
`Code` (from catalogue), `Message`, optional `Detail` (never contains secrets).

## Error-code catalogue (constants)
`DATE_UNRECOGNIZED`, `DATE_INVALID`, `DATE_OUT_OF_RANGE`, `DATE_DUPLICATE`, `API_NO_PHOTOS`, `API_RATE_LIMITED`, `API_AUTH`, `API_UNAVAILABLE`, `API_BAD_RESPONSE`, `DL_FAILED`, `DL_REJECTED_URL`, `DL_TOO_LARGE`, `IO_ERROR`, `CONFIG_MISSING_KEY`, `INPUT_FILE_ERROR`.

## Relationships and state
- A `RunSummary` has one `DateResult` per `DateInputLine` (in order); a `DateResult` has 0..N `PhotoOutcome`.
- `DateResult` status derivation: invalid / unrecognized / outside-range / duplicate are terminal before any lookup; otherwise the provider result gives `NoPhotos` | `SourceUnavailable` | downloads → `Success` (all downloaded or skipped) | `PartialFailure` (some failed) | `Failed` (all failed).
- Photo state per file: `absent → .part (in progress) → final` (atomic rename); a `.part` never counts as existing.

## Configuration model (`NasaOptions`, `RunOptions`)
| Key | Default | Validation |
|---|---|---|
| `Nasa:ApiKey` (`NASA__ApiKey`) | none | required, ≥ 8 chars, never logged |
| `Nasa:PrimaryBaseUrl` | `https://api.nasa.gov/mars-photos/api/v1/` | https absolute |
| `Nasa:FallbackBaseUrl` | `https://mars.nasa.gov/rss/api/` | https absolute |
| `Run:Rover` | `curiosity` | one of curiosity/opportunity/spirit |
| `Run:PhotosPerDate` | 5 | 3–5 |
| `Run:InputFile` | `dates.txt` | |
| `Run:OutputRoot` | `photos` | writable |
| `Run:LookupTimeoutSeconds` / `DownloadTimeoutSeconds` | 30 / 60 | > 0 |
| `Run:MaxRetries` | 3 | 0–5 |
| `Run:MaxParallelDownloads` | 3 | 1–5 |
| `Run:MaxImageBytes` | 26214400 | > 0 |
| `Run:AllowedImageHosts` | `mars.nasa.gov`, `mars.jpl.nasa.gov` | exact host match |

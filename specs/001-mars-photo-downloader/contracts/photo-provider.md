# Contract: Photo Provider Port and External Sources

## Application port
```text
IPhotoProvider
  Name : string                                  // "MarsRoverPhotos" | "RawImageFeed"
  GetPhotosAsync(rover, date, maxPhotos, ct) -> ProviderResult

ProviderResult =
  | Photos(IReadOnlyList<RoverPhoto> selected, int available)   // at least 1
  | NoPhotos()                                                  // valid empty answer, NO fallback
  | Unavailable(errorCode, message, retriable)                  // chain tries next provider
```
Rules: never throws for expected failures; honors cancellation; never logs the key; returns photos sorted by `Id` (numeric when every id is a whole number, else ordinal) and truncated to `maxPhotos` (`available` = count before truncation).

`ProviderChain` = [Primary, Fallback]. `Photos` / `NoPhotos` stops the chain; `Unavailable` moves on and records `FallbackReason`. Both unavailable → `DateResult.Status = SourceUnavailable` with `API_UNAVAILABLE`.

## External source 1: Mars Rover Photos (primary)
`GET {PrimaryBaseUrl}rovers/{rover}/photos?earth_date=yyyy-MM-dd&api_key={key}`

| Response | Classification |
|---|---|
| 200 JSON `{"photos":[...]}` non-empty | `Photos` |
| 200 JSON `{"photos":[]}` | `NoPhotos` |
| 200 non-JSON / malformed / missing `photos` | `Unavailable(API_BAD_RESPONSE)` |
| 404 or HTML body (observed live: "No such app") | `Unavailable(API_UNAVAILABLE)`, no retry |
| 403 | `Unavailable(API_AUTH)`, no retry |
| 429 | retry honoring `Retry-After`, then `Unavailable(API_RATE_LIMITED)` |
| 5xx / timeout / network error | retry with backoff, then `Unavailable(API_UNAVAILABLE)` |

DTO is tolerant of unknown fields. Mapping: `id` → Id (+ camera name in the file stem), `sol`, `camera.name`, `img_src`, `earth_date`, `rover.name`.

## External source 2: raw image feed (fallback, Curiosity only)
`GET {FallbackBaseUrl}?feed=raw_images&category=msl&feedtype=json&sol={sol}` (no key)

- `sol = floor((date@12:00Z − 2012-08-06T05:17:57Z) / 88775.244 s)` — decision D-1, approved 2026-10-06. Fixtures: 2017-02-27 → 1621, 2018-06-02 → 2069, 2016-07-13 → 1398.
- Fields used: `images[].imageid`, `sol`, `date_taken_utc`, `camera.instrument`, `image_files.full_res|large`.
- Mapping: `Id = imageid`; URL = `full_res`, else `large`, rewritten to `https`; `EarthDate` = requested date.
- A rover other than `curiosity` → `Unavailable(API_UNAVAILABLE, "fallback supports Curiosity only")`.
- Empty `images` → `NoPhotos`.

## Download port
```text
IPhotoDownloader.DownloadAsync(RoverPhoto, destination, ct) -> DownloadResult
```
Validates `UrlPolicy` (https, exact allow-listed host, no userinfo, default port only), follows at most 3 redirects and only to allow-listed hosts, requires `image/*`, and enforces `MaxImageBytes` while streaming.

## Store port
```text
IPhotoStore.Exists(date, fileName) -> bool          // exists && length > 0
IPhotoStore.WriteAsync(date, fileName, stream, ct)  // .part then atomic move; cleans up on failure
```

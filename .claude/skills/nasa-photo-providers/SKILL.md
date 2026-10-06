---
name: nasa-photo-providers
description: Facts about the NASA photo sources (dead Mars Rover Photos API, working raw-image feed, earth-date to sol conversion). Use when implementing or testing photo providers.
---
- Primary (per brief): `GET https://api.nasa.gov/mars-photos/api/v1/rovers/{rover}/photos?earth_date=YYYY-MM-DD&api_key=KEY`. VERIFIED 2026-10-06: returns HTTP 404 HTML "No such app" for all routes. Must classify as API_UNAVAILABLE/API_BAD_RESPONSE, never throw on HTML.
- Fallback: `https://mars.nasa.gov/rss/api/?feed=raw_images&category=msl&feedtype=json&sol={sol}`; no key; fields `images[].image_files.{small,medium,large,full_res}`, `imageid`, `date_taken_utc`, `camera.instrument`.
- Curiosity sol: landing 2012-08-06T05:17:57Z, sol = floor(seconds / 88775.244). Query sol-1..sol+1, filter `date_taken_utc` date == requested date. 2017-02-27 -> 1621, 2018-06-02 -> 2069, 2016-07-13 -> 1398.
- Image URLs are http on mars.nasa.gov / mars.jpl.nasa.gov: allow-list hosts, try https first.
- api.nasa.gov key limit 2000/h; transient 500s occur (retry). Tests use recorded fixtures, never live calls.

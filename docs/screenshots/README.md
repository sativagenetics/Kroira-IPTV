# Repository Screenshots

The current V2 Store-ready screenshot set is committed under [store](store/). These are the repository-facing screenshots for Store review and public release documentation.

## Current V2 Store-Ready Set

| Order | Screen | File |
| --- | --- | --- |
| 01 | Home | [store/01-home.png](store/01-home.png) |
| 02 | Live TV | [store/02-live-tv.png](store/02-live-tv.png) |
| 03 | Movies | [store/03-movies.png](store/03-movies.png) |
| 04 | Series | [store/04-series.png](store/04-series.png) |
| 05 | Sources | [store/05-sources.png](store/05-sources.png) |
| 06 | EPG Center | [store/06-epg-center.png](store/06-epg-center.png) |
| 07 | Settings | [store/07-settings.png](store/07-settings.png) |
| 08 | Player | [store/08-player.png](store/08-player.png) |

[store/social-preview.png](store/social-preview.png) is the companion social/repository preview image, not part of the ordered eight-screenshot Store set.

Generated smoke-test and Store-review captures belong under `artifacts/` until they are manually reviewed, sanitized, and copied here intentionally.

## Rules

- Use a sanitized/sample data set only.
- Do not commit temporary debug captures or ad hoc verification artifacts.
- Do not show provider credentials, playlist URLs, MAC addresses, personal profile names, private source names, unlicensed provider branding, channel logos, or copyrighted artwork.
- Do not imply that KROIRA provides free TV, included channels, provider accounts, subscriptions, credentials, or media content.
- Prefer stable PNG captures with product-grade framing and consistent aspect ratios.
- Keep source files small enough for normal repository review.

## Store Capture Workflow

Generated store-review captures belong under `artifacts/store-screenshots` and are ignored by git.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\store-screenshots.ps1 -Configuration Release -Platform x64 -SanitizedDataConfirmed
```

Inspect every generated image before copying approved screenshots into [store](store/).

## Content Disclaimer

KROIRA IPTV does not include, sell, provide, host, curate, or distribute channels, streams, playlists, credentials, subscriptions, movies, series, or other media content. Users are responsible for authorized sources, and KROIRA does not bypass DRM, paywalls, authentication, provider restrictions, or access controls. Screenshots must not imply otherwise.

Keep the root [README.md](../../README.md) screenshot section aligned with this Store-ready set.

# KROIRA IPTV

KROIRA IPTV is a modern packaged Windows IPTV player built with WinUI 3 and .NET 8. It is designed for users who already have authorized IPTV sources and want a polished local app for source management, catalog browsing, EPG, metadata, diagnostics, and embedded playback.

KROIRA is not a content service. It does not provide, host, sell, curate, or distribute channels, playlists, subscriptions, credentials, movies, series, or live streams. Users must bring their own legal source.

[Contributing](CONTRIBUTING.md) | [Code of Conduct](CODE_OF_CONDUCT.md) | [Security](SECURITY.md) | [Support](SUPPORT.md) | [Privacy](docs/privacy.html) | [V2 Status](docs/v2_release_status.md)

## Current Status

`main` currently carries the V2 release-candidate baseline.

- Public release version: `2.0.0`
- Package manifest identity version: `2.0.0.4`
- App assembly/file version: `2.0.0.0`
- Primary platform: Windows desktop, packaged MSIX, WinUI 3, .NET 8
- Release posture: active V2 release-candidate validation, not long-term maintenance mode

The current validation baseline covers restore, Debug x64 build, unit tests, Release regression corpus, localization checks, and unsigned local MSIX packaging with signing disabled where appropriate.

Run the V2 release gate locally:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-v2-release.ps1 -Configuration Debug -Platform x64
```

Create an unsigned local release-candidate package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-v2-release.ps1 -Unsigned -SkipValidation -Configuration Release -Platform x64
```

Unsigned packages are for local review only and should not be uploaded to Partner Center.

## What KROIRA Is

- A Windows desktop IPTV player for user-provided sources
- A local-first source manager and catalog organizer
- A Live TV, Movies, Series, Favorites, Continue Watching, Search, Guide, Sources, Settings, and Profile experience
- A packaged WinUI 3 application built on .NET 8 and Windows App SDK
- An embedded playback app based on `mpv/libmpv`
- A diagnostics surface for understanding provider/source health, guide coverage, logos, sync results, and playback readiness

## What KROIRA Is Not

- Not an IPTV reseller, subscription provider, or content provider
- Not a channel directory, playlist index, stream host, or credential broker
- Not a way to bypass DRM, paywalls, authentication, provider restrictions, or access controls
- Not a guarantee that a third-party source is lawful, stable, complete, or compatible
- Not responsible for the EPG, logos, categories, metadata, availability, or reliability supplied by a user source

## Feature Set

### Source Management

- Add and manage user-provided M3U playlists, Xtream providers, and supported provider portal profiles
- Use local files or remote URLs for M3U playlists
- Configure Xtream server URL, username, and password
- Configure provider-dependent portal details where supported by the app
- Re-sync sources and guide data from the Sources and Guide surfaces
- Review safe-to-share source activity summaries and diagnostics
- Store source settings, credentials, guide state, preferences, favorites, and progress locally on the device

### Library And Navigation

- Live TV browsing with provider categories, smart groupings, search, and focus-friendly navigation
- Movies and Series catalog surfaces backed by provider VOD data
- Favorites across live channels, movies, and series
- Continue Watching for resumable VOD playback
- Global search across synced live channels, movies, series, and playable episodes
- Profile-related local state for favorites, resume data, and access-related controls
- Runtime localization support across the app shell and major surfaces

### Playback

- Embedded in-app playback for live TV, movies, and episodes
- `mpv/libmpv` playback pipeline through `MpvPlayer`, `NativeMpv`, and `VideoSurface`
- Fullscreen playback, double-click fullscreen toggle, and auto-hiding playback chrome
- Keyboard and remote-friendly playback controls
- Seek behavior for VOD and live-edge behavior for live streams
- Volume, mute, speed, aspect, deinterlace, screenshot, and picture-in-picture support where available
- Audio track and subtitle track selection
- Subtitle delay, subtitle scale, subtitle position, and external subtitle loading support
- Item inspection and external handoff support for troubleshooting and advanced workflows

### EPG And Metadata

- XMLTV guide support for detected, manual override, fallback/enrichment, and no-guide flows
- EPG coverage reporting, weak match review, manual channel matching, and guide timeline views
- Provider logos, categories, current/next programme details, and catch-up signals when the source exposes them
- TMDb metadata enrichment/fallback for movies and series when configured and when provider data is incomplete
- Conservative fallback behavior when provider metadata, artwork, or guide data is missing

### Diagnostics And Health

- Source health reports for catalog quality, guide coverage, logo coverage, duplicates, suspicious entries, probes, and sync outcomes
- Source repair guidance for common import, authentication, guide, and routing problems
- Sanitized diagnostics designed to mask playlist URLs, usernames, passwords, tokens, keys, MAC-like values, and loose secrets before display or export
- Deterministic regression corpus for M3U, Xtream, supported portal sources, XMLTV, source health, catch-up detection, and playback URL resolution

## Supported Source Types

| Source type | Input model | Guide behavior | Notes |
| --- | --- | --- | --- |
| M3U | Remote URL or local playlist file | Detected XMLTV, manual XMLTV override, fallback XMLTV URLs, or no guide | Provider categories, logos, and stream reliability depend on the playlist |
| Xtream | Server URL, username, password | Provider-derived guide URL, manual XMLTV override, fallback XMLTV URLs, or no guide | Live, VOD, and series availability depend on the account/source |
| Provider portal profiles | Portal URL plus provider-specific profile details where supported | Manual XMLTV override, fallback XMLTV URLs, or no guide | Provider-dependent behavior; only use sources you are authorized to access |

## Screenshots

Curated repository screenshots are not committed yet. Temporary debug captures and generated Store-review captures belong under `artifacts/` and are ignored by git.

When stable, sanitized screenshots are approved for public use, place them under [docs/screenshots](docs/screenshots/README.md) and update this section with relative image links.

For local visual QA:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\visual-smoke.ps1 -Configuration Debug -Platform x64
```

For Store/release screenshot review:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\store-screenshots.ps1 -Configuration Release -Platform x64 -SanitizedDataConfirmed
```

Public screenshots must use sanitized/sample data that is authorized for public display. See [Store Screenshot Workflow](docs/store_screenshot_workflow.md).

## Installation And Build

### Requirements

- Windows 10 version 1809 or newer, or Windows 11
- .NET SDK compatible with [global.json](global.json), currently .NET `8.0.400` with latest feature roll-forward
- Visual Studio 2022 or newer with Windows App SDK / WinUI workload support
- Windows App SDK runtime compatible with the project package references

### Run From Visual Studio

1. Open [Kroira.sln](Kroira.sln).
2. Select the packaged `Kroira.App` startup profile.
3. Build for `x64`.
4. Run or debug the packaged app from Visual Studio.

KROIRA expects package identity for normal app flows. Do not use `bin\...\Kroira.App.exe` as the primary smoke-test path.

### Build From PowerShell

```powershell
dotnet restore Kroira.sln
dotnet build Kroira.sln -c Debug -p:Platform=x64 -p:AppxPackageSigningEnabled=false
```

Launch the packaged debug build outside the IDE:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\launch-packaged-debug.ps1
```

### Tests And Regression Validation

Run unit tests:

```powershell
dotnet test Kroira.sln -c Debug -p:Platform=x64 -p:AppxPackageSigningEnabled=false
```

Run the deterministic regression corpus:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-regressions.ps1
```

Run the CI-equivalent local validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ci-regressions.ps1 -Configuration Release
```

## Repository Layout

- `src/Kroira.App/` - packaged WinUI 3 Windows app
- `src/Kroira.App/Services/Playback/` - embedded `mpv/libmpv` playback pipeline
- `src/Kroira.App/Services/Parsing/` - source and guide parsing pipeline
- `tests/Kroira.UnitTests/` - unit and service-level validation
- `tests/Kroira.Regressions/` - deterministic ingestion and playback-resolution regression corpus
- `scripts/` - build, validation, packaging, screenshot, and release helpers
- `docs/` - public support/privacy pages, Store submission copy, release checklist, and product docs
- `.github/` - issue templates, pull request template, and GitHub Actions workflows

## Store And Release Notes

Store-supporting copy lives in:

- [docs/store_submission_info.md](docs/store_submission_info.md)
- [docs/store_release_checklist.md](docs/store_release_checklist.md)
- [docs/store_screenshot_workflow.md](docs/store_screenshot_workflow.md)
- [RELEASE_NOTES_v2.0.0.md](RELEASE_NOTES_v2.0.0.md)
- [docs/privacy.html](docs/privacy.html)
- [docs/support.html](docs/support.html)

Keep Partner Center copy aligned with those files and the in-app About/Settings legal text.

## Honest Limitations

- KROIRA starts empty until the user adds an authorized source.
- Source quality varies. Channel availability, VOD catalogs, EPG coverage, logos, categories, catch-up support, subtitles, audio tracks, and stream reliability all depend on the user source.
- TMDb enrichment is optional fallback metadata behavior and does not replace missing provider rights or stream access.
- Curated public screenshots are not yet committed.
- Recording, download, restore, and media-library workflows exist in the codebase but are release-gated and are not part of the default public V2 RC surface.
- Store signing and Partner Center identity must be handled outside the unsigned local packaging script.

## Roadmap

Near-term V2 polish:

- Playback stability, error messaging, and player UX polish
- Source diagnostics, repair guidance, and guide-quality improvements
- Search, filtering, sorting, and metadata-quality improvements across catalog surfaces
- Sanitized screenshot and release-asset preparation
- Store review readiness and public documentation polish

Longer-term areas under evaluation:

- Productizing currently gated recording, download, restore, and media-library workflows
- Additional remote-first and desktop-first quality-of-life improvements
- Broader provider-quirk coverage in the deterministic regression corpus

## Legal Disclaimer

KROIRA IPTV is a media player and source manager for user-provided sources.

It does not include, sell, provide, host, curate, or distribute:

- channels
- streams
- playlists
- stream credentials
- subscription services
- movies, series, or other media content

Users are responsible for adding only authorized sources and for complying with applicable provider terms and laws. KROIRA does not bypass DRM, paywalls, authentication, provider restrictions, or access controls.

## License

This repository is licensed under the [MIT License](LICENSE).

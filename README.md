# KROIRA IPTV

KROIRA IPTV is a packaged WinUI 3 desktop app for Windows that lets users connect their own IPTV sources, browse live TV and VOD, and play content through an embedded `mpv/libmpv` pipeline.

It is a player-only product. The repository contains the Windows client, deterministic regression coverage for the ingestion pipeline, and the supporting documentation used to keep the product surface honest and maintainable.

[Contributing](CONTRIBUTING.md) | [Code of Conduct](CODE_OF_CONDUCT.md) | [Security](SECURITY.md) | [Support](SUPPORT.md)

## Product Summary

KROIRA focuses on three primary content surfaces:

- Live TV
- Movies
- Series

The current restored codebase includes source onboarding, source health and guide controls, favorites, continue watching, embedded playback, and local configuration/state persistence.

## What KROIRA Is

- A Windows desktop IPTV library and player for user-provided sources
- A packaged WinUI 3 application built on .NET 8
- A local-first client that stores source configuration, playback progress, favorites, and settings on the device
- A single-path embedded playback app based on `mpv/libmpv`

## What KROIRA Is Not

- Not a content service, IPTV reseller, or subscription provider
- Not a source directory or playlist distributor
- Not a repository of channels, stream URLs, or user credentials
- Not a guarantee that third-party providers or streams are lawful, stable, or compatible

## Feature Overview

### Source Management

- Add and manage M3U playlists, Xtream providers, and Stalker portals
- Configure guide mode per source with detected, manual XMLTV override, or no-guide behavior
- Review source health, diagnostics, repair guidance, and safe-to-share activity summaries
- Re-sync sources and guide data from the app surface

### Library Surfaces

- Browse live TV with categories and focus-friendly navigation
- Browse movies and series with provider metadata plus enrichment/fallback handling
- Save and revisit favorites
- Resume VOD content through Continue Watching

### Playback

- Embedded in-app playback for live TV, movies, and episodes
- Fullscreen support and double-click fullscreen toggle
- Live-edge behavior for live streams and seek behavior for VOD
- Auto-hiding playback chrome and keyboard/remote-friendly navigation
- Item inspection and external handoff support where applicable

### Guides, Metadata, and State

- XMLTV guide support with provider-detected and manual override flows
- Playback progress persistence for VOD content
- Local settings for appearance, remote-friendly navigation, and backup/export behavior
- TMDb-backed metadata enrichment/fallback when provider metadata is incomplete

## Supported Source Types

| Source type | Current input model | Guide handling |
| --- | --- | --- |
| M3U | URL or local file playlist | Detected XMLTV when available, manual XMLTV override, or no guide |
| Xtream | Server URL, username, password | Provider-derived guide URL, manual XMLTV override, or no guide |
| Stalker Portal | Portal URL plus MAC address, with optional device metadata | Manual XMLTV override or no guide, with provider-aware routing controls |

## Playback Architecture

The current baseline uses a single embedded playback path:

- `EmbeddedPlaybackPage` hosts the active playback UI
- `VideoSurface` owns the rendering surface
- `MpvPlayer` manages player lifecycle and commands
- `NativeMpv` handles the native interop boundary

This repository should be treated as `mpv/libmpv`-based. Older mixed or transitional playback approaches are not part of the active product baseline.

## Screenshots

Curated repository screenshots are not committed yet. Temporary debug captures are intentionally excluded from git.

When stable, sanitized screenshots are ready, place them under [docs/screenshots](docs/screenshots/README.md) and update this section with relative image links.

## Getting Started

### Requirements

- Windows 10 or Windows 11
- .NET 8 SDK
- Visual Studio 2022 or newer
- Windows App SDK compatible development environment

### Run the App

1. Open `Kroira.sln` in Visual Studio.
2. Select the packaged startup profile for `Kroira.App`.
3. Build for `x64`.
4. Run or debug from Visual Studio.

For packaged debug launches outside the IDE:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\launch-packaged-debug.ps1
```

Do not use `bin\...\Kroira.App.exe` as the primary smoke-test path. This app expects package identity.

## Development and Build

### Build

```powershell
dotnet build Kroira.sln -c Debug -p:Platform=x64
```

### Regression Suite

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-regressions.ps1
```

Full CI-equivalent validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\ci-regressions.ps1 -Configuration Release
```

### Repository Layout

- `src/Kroira.App/` - packaged WinUI 3 application
- `tests/Kroira.Regressions/` - deterministic ingestion and pipeline regression corpus
- `scripts/` - local build, launch, and validation helpers
- `docs/` - public support/privacy pages and product docs

## Current Status

The repository is in active product development, not long-term maintenance mode.

Current baseline:

- Source onboarding and source management are active
- Live TV, movies, series, favorites, and continue watching are active
- Embedded playback is active and based on `mpv/libmpv`
- Guide settings, source health, and repair surfaces are active

Current limitations:

- Curated screenshots and polished public release assets are not yet committed
- Some capture-library surfaces exist in code, but download, recording, restore, and media-library UI are feature-gated and not part of the default public product surface in the restored baseline

## Roadmap

Near-term priorities:

- Playback hardening and UX polish around the embedded player
- Source diagnostics, repair flows, and guide-quality improvements
- Search, filtering, sorting, and metadata-quality improvements across catalog surfaces
- Better public release assets, contributor docs, and repo presentation

Longer-term areas under evaluation:

- Carefully productizing currently gated recording and download workflows
- Additional quality-of-life improvements for remote-first and desktop-first usage

## Disclaimer

KROIRA IPTV is a client application only.

It does not include, sell, provide, host, curate, or distribute:

- channels
- playlists
- stream credentials
- subscription services
- copyrighted content

Users are responsible for the legality, availability, and safety of the sources they add.

## License

This repository is licensed under the [MIT License](LICENSE).

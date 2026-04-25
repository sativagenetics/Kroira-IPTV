# KROIRA V2 Release Status

This document summarizes the current V2 release-candidate state for public GitHub visitors, Store reviewers, contributors, and maintainers.

## Release Identity

- Product name: KROIRA IPTV
- Public release version: `2.0.0`
- Package manifest identity version: `2.0.0.4`
- App assembly/file version: `2.0.0.0`
- Current baseline branch: `main`
- Primary platform: packaged Windows desktop app, WinUI 3, .NET 8, Windows App SDK

## Product Positioning

KROIRA is a local Windows IPTV player, source manager, catalog organizer, EPG/metadata viewer, and playback surface for user-provided sources.

KROIRA does not provide, host, sell, curate, or distribute channels, playlists, subscriptions, credentials, movies, series, or live streams. Users are responsible for adding only authorized sources and complying with provider terms and applicable law.

## Completed V2 Areas

- Source onboarding and management for M3U playlists, Xtream providers, and supported provider portal profiles
- Live TV, Movies, Series, Favorites, Continue Watching, Search, Guide, Sources, Settings, Profile, and About surfaces
- Embedded `mpv/libmpv` playback pipeline with in-app player controls
- Audio and subtitle track selection, subtitle timing/style controls, fullscreen, and picture-in-picture support where available
- XMLTV guide discovery, manual override, fallback guide URLs, EPG timeline, coverage reports, and manual channel matching
- TMDb metadata enrichment/fallback when configured
- Source diagnostics, health scoring, repair guidance, source activity summaries, and sanitized export language
- Local profile-aware favorites, progress, settings, and app state
- Localization resources and runtime language handling across major surfaces
- Unit tests, deterministic regression corpus, CI workflows, release validation scripts, and unsigned local packaging scripts
- Store-facing privacy, support, disclaimer, screenshot, and submission guidance

## Remaining Polish Items

- Approve and commit sanitized public screenshots under `docs/screenshots/`
- Reconfirm Partner Center package identity, publisher identity, signing, Store pricing, and capability justifications before upload
- Continue playback hardening around provider-specific stream failures and edge-case codecs
- Expand source diagnostics and guide repair guidance as provider quirks are discovered
- Keep localization quality improving across all supported languages
- Productize or keep gated the recording, download, restore, and media-library workflows before presenting them as public features

## Release-Readiness Checklist

- [ ] `src/Kroira.App/Package.appxmanifest` version matches the intended package upload version
- [ ] `src/Kroira.App/Kroira.App.csproj`, [RELEASE_NOTES_v2.0.0.md](../RELEASE_NOTES_v2.0.0.md), and Store copy agree on release identity
- [ ] [docs/store_submission_info.md](store_submission_info.md) matches Partner Center copy
- [ ] [docs/privacy.html](privacy.html) and [docs/support.html](support.html) are published and reachable
- [ ] `runFullTrust` and `internetClient` capability justifications are current
- [ ] Sanitized screenshots are reviewed for private URLs, credentials, provider brands, channel logos, copyrighted posters, and personal profile names
- [ ] Full validation passes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-v2-release.ps1 -Configuration Debug -Platform x64
```

- [ ] Unsigned local package is generated only for local review:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-v2-release.ps1 -Unsigned -SkipValidation -Configuration Release -Platform x64
```

- [ ] Store upload uses a trusted signed package, not the unsigned local artifact

## Current Limitations

- KROIRA opens without content until the user adds an authorized source.
- Source catalogs, guide data, logos, metadata, categories, subtitles, audio tracks, and stream reliability depend on the user source.
- TMDb is metadata enrichment only; it does not provide stream access or content rights.
- Some advanced workflows exist in code but are feature-gated for the default V2 RC public surface.
- Public screenshots are pending final sanitized capture and approval.

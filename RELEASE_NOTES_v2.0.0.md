# KROIRA IPTV 2.0.0 Release Notes

## Scope

This release establishes the V2 release-candidate baseline for KROIRA IPTV as a packaged WinUI 3 / .NET 8 Windows IPTV player.

- Public release version is `2.0.0`.
- Package manifest identity version is currently `2.0.0.4`.
- App assembly and file metadata are versioned as `2.0.0.0`.
- The default public surface is a player, catalog organizer, EPG/metadata viewer, source diagnostics surface, and source manager for user-provided sources.

## Highlights

- Source onboarding and management for M3U playlists, Xtream providers, and supported provider portal profiles.
- Live TV, Movies, Series, Favorites, Continue Watching, Search, Guide, Sources, Settings, Profile, and About surfaces.
- Embedded `mpv/libmpv` playback with fullscreen, live-edge/VOD seek behavior, picture-in-picture support, audio track selection, subtitle track selection, subtitle timing/style controls, and player error mapping.
- EPG V2 discovery, XMLTV parsing, identity matching, manual override, fallback guide URLs, coverage metrics, weak match review, and timeline guide support.
- Source Import V2 hardening for M3U, Xtream, and supported portal parsing, diagnostics, health scoring, bounded probes, repair guidance, and protected credential storage.
- TMDb metadata enrichment/fallback when configured and when provider metadata is incomplete.
- Profile-aware favorites, playback progress, continue-watching state, settings, and local app preferences.
- Localization resources and runtime language handling across major app surfaces.
- EF Core migration/bootstrap hardening, V2 data integrity checks, release scripts, Store/legal docs, unit tests, regression corpus, and CI workflows.

## Legal And Content Position

KROIRA IPTV is a media player and source manager for user-provided sources. It does not include, sell, provide, host, curate, or distribute channels, streams, playlists, credentials, subscriptions, movies, series, or other media content.

Users are responsible for adding only authorized sources and complying with applicable provider terms and laws. KROIRA does not bypass DRM, paywalls, authentication, provider restrictions, or access controls.

## Known Limitations

- KROIRA starts empty until the user adds an authorized source.
- EPG, logos, categories, metadata, subtitles, audio tracks, catch-up support, and stream reliability depend on the configured source.
- Curated public screenshots are not yet committed.
- Recording, download, restore, and media-library workflows exist in the codebase but are release-gated and are not part of the default public V2 RC surface.
- Unsigned local packages are for review/testing only and are not Store upload artifacts.

## Validation

Before creating release artifacts, run:

```powershell
dotnet restore Kroira.sln
dotnet build Kroira.sln -c Debug -p:Platform=x64 -p:AppxPackageSigningEnabled=false --no-restore /m:1 -p:BuildInParallel=false
dotnet test Kroira.sln -c Debug -p:Platform=x64 -p:AppxPackageSigningEnabled=false --no-restore /m:1 -p:BuildInParallel=false
```

For the full local release gate, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-v2-release.ps1 -Configuration Debug -Platform x64
```

For unsigned local release-candidate packaging:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-v2-release.ps1 -Unsigned -SkipValidation -Configuration Release -Platform x64
```

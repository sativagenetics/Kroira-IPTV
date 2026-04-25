# KROIRA IPTV 2.0.0 Release Notes

## Scope

This release establishes the V2 release-candidate baseline for KROIRA IPTV.

- App and package metadata are versioned as `2.0.0` / `2.0.0.0`.
- Player V2 overlay, hotkey routing, error mapping, and embedded-player integration are in place.
- EPG V2 discovery, XMLTV parsing, identity matching, manual override, coverage metrics, and timeline guide support are in place.
- Source Import V2 hardens M3U/Xtream/Stalker parsing, diagnostics, health scoring, and protected credential storage.
- Global search, metadata fallback, favorites, continue watching, profiles, and UI state polish are included.
- EF Core migrations, schema repair, data cleanup, release scripts, Store/legal docs, unit tests, regression corpus, and CI workflows are refreshed for V2.

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

## Content Disclaimer

KROIRA IPTV is a media player and source manager for user-provided sources. It does not include, sell, provide, host, curate, or distribute channels, streams, playlists, credentials, subscriptions, or media content. Users are responsible for adding only authorized sources and complying with applicable provider terms and laws. KROIRA does not bypass DRM, paywalls, authentication, or access controls.

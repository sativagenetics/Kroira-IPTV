# KROIRA IPTV 2.0.0 Release Notes

## Scope

This release establishes the V2 release baseline for KROIRA IPTV.

- App and package metadata are versioned as `2.0.0` / `2.0.0.0`.
- The solution includes a unit test project for deterministic, pure utility smoke coverage.
- Release validation and unsigned package helper scripts are available under `scripts/`.
- Store release and screenshot preparation docs have been refreshed for the V2 baseline.

## Validation

Before creating release artifacts, run:

```powershell
dotnet restore Kroira.sln
dotnet build Kroira.sln -c Debug -p:Platform=x64 -p:AppxPackageSigningEnabled=false --no-restore /m:1 -p:BuildInParallel=false
dotnet test Kroira.sln -c Debug -p:Platform=x64 -p:AppxPackageSigningEnabled=false --no-restore /m:1 -p:BuildInParallel=false
```

For the full local release gate, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-release.ps1 -Configuration Release
```

## Content Disclaimer

KROIRA IPTV is a media player utility for user-provided sources. It does not include, sell, provide, host, curate, or distribute channels, playlists, stream credentials, subscriptions, or copyrighted content. Users are responsible for the legality, availability, and safety of the sources they add.

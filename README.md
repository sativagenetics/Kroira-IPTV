# KROIRA IPTV

KROIRA IPTV is a Windows desktop IPTV player built with C# / .NET 8 / WinUI 3.

The goal of the project is simple: let users add their own M3U or Xtream source, browse live TV / movies / series, and watch content inside an embedded player experience on Windows. The app is intended to remain a **player-only** product. It does not provide content, playlists, or credentials. :contentReference[oaicite:1]{index=1} :contentReference[oaicite:2]{index=2}

## Current status

The project has moved past the early scaffold stage and already includes:

- source onboarding for M3U / Xtream
- live channels browsing
- movies and series browsing
- embedded playback using **WinUI 3 MediaPlayerElement** as the primary playback path
- fullscreen / F11 / Esc / double-click playback controls
- favorites
- continue watching
- XMLTV EPG foundation
- SQLite persistence with EF Core migrations :contentReference[oaicite:3]{index=3}

Recent work focused on stabilizing the core experience:

- Xtream VOD cleanup to reduce garbage / placeholder / non-playable entries
- preserving favorites and continue watching across VOD syncs using stable external IDs
- episode progress preservation improvements during series sync
- XMLTV parsing hardening and safer EPG channel matching
- duplicate XMLTV channel id guard
- repo cleanup with `.gitignore` and build artifact untracking :contentReference[oaicite:4]{index=4} :contentReference[oaicite:5]{index=5} :contentReference[oaicite:6]{index=6}

## Tech stack

- C#
- .NET 8
- WinUI 3
- Windows App SDK
- SQLite
- Entity Framework Core
- CommunityToolkit.Mvvm
- DI + MVVM architecture :contentReference[oaicite:7]{index=7}

## Playback architecture

The app originally experimented with LibVLCSharp, but the main playback direction was changed because the desired embedded playback behavior in WinUI 3 was not reliable enough through that path.

**Primary playback path:**
- WinUI 3 `MediaPlayerElement`

LibVLC should be treated as legacy / fallback territory, not the main direction of the app. :contentReference[oaicite:8]{index=8}

## Features

### Sources
- Add M3U sources
- Add Xtream sources
- Persist source profiles locally
- Delete sources and related imported data safely :contentReference[oaicite:9]{index=9}

### Live TV
- Channel list
- Category filtering
- Channel favorites
- Embedded playback :contentReference[oaicite:10]{index=10}

### Movies & Series
- Xtream VOD import
- Movies page
- Series page
- VOD cleanup / filtering improvements
- Better identity stability across syncs :contentReference[oaicite:11]{index=11} :contentReference[oaicite:12]{index=12}

### Continue Watching
- Channel support
- Movie support
- Episode support
- Progress preservation improvements across syncs :contentReference[oaicite:13]{index=13} 

### EPG
- XMLTV URL support
- EPG import / sync foundation
- Safer XMLTV date parsing
- Better normalized channel matching
- Duplicate channel id guard :contentReference[oaicite:15]{index=15} :contentReference[oaicite:16]{index=16}

## Getting started

### Requirements

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 or newer recommended
- Windows App SDK compatible development environment

### Run

```bash
dotnet run --project src/Kroira.App/Kroira.App.csproj

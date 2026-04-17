# KROIRA IPTV

KROIRA IPTV is a modern Windows IPTV player built with C# / .NET 8 / WinUI 3.

The app is designed as a **player-only** product. It does not provide playlists, channels, or credentials. Users bring their own sources and use the app to browse and watch content through a polished desktop experience.

## Overview

KROIRA IPTV focuses on three core content types:

- **Live TV**
- **Movies**
- **Series**

The app supports user-provided IPTV sources such as:

- **M3U**
- **Xtream**

The current playback system is built around a rebuilt **mpv/libmpv-based embedded player** for Windows.

## Current Status

The project is now past the early scaffold stage and has a working core experience for:

- source onboarding for M3U / Xtream
- live TV browsing
- movie browsing
- series browsing
- favorites
- continue watching
- XMLTV / EPG foundation
- embedded playback inside the app window

The playback system has recently been rebuilt and stabilized around **mpv/libmpv**.

### Playback features currently working

- Live TV playback
- VOD movie playback
- Series episode playback
- Embedded in-app playback
- Back / Stop behavior
- Fullscreen
- Double-click fullscreen
- VOD seek
- Continue Watching / resume playback
- Volume controls
- Improved playback chrome auto-hide
- LIVE button / live-edge behavior for live streams

## Playback Architecture

KROIRA IPTV now uses a rebuilt mpv-based playback path.

### Active playback flow

- `EmbeddedPlaybackPage`
- `MpvPlayer`
- `VideoSurface`
- `NativeMpv`

### Design goals

- stable embedded playback inside the app window
- no detached external player window
- safe teardown and re-entry
- good behavior across live TV, movies, and episodes
- clear separation between live-stream and VOD behavior

### Notes

- The player is intentionally single-path and mpv-based.
- Old mixed or transitional playback paths should not be treated as active architecture.
- Future playback work should be incremental hardening and UX improvement, not another full rewrite unless absolutely necessary.

## Features

### Sources

- Add M3U sources
- Add Xtream sources
- Persist source profiles locally
- Remove sources safely

### Live TV

- Channel browsing
- Category filtering
- Favorites
- Embedded playback
- LIVE button / go-to-live behavior
- Non-seekable live policy where appropriate
- EPG foundation

### Movies & Series

- Movies page
- Series page
- Embedded playback
- VOD seek
- Resume support
- Continue Watching integration

### Continue Watching

- Resume support for movies
- Resume support for episodes
- Progress persistence for VOD content

### Playback UX

- Embedded playback inside the app window
- Fullscreen support
- Double-click fullscreen
- Auto-hide playback chrome
- Volume control
- Back / Stop flow improvements

### EPG

- XMLTV URL support
- Current / upcoming program foundation
- Basic guide-aware channel experience

## Tech Stack

- C#
- .NET 8
- WinUI 3
- Windows App SDK
- SQLite
- Entity Framework Core
- CommunityToolkit.Mvvm

## Getting Started

### Requirements

- Windows 10 / 11
- .NET 8 SDK
- Visual Studio 2022 or newer
- Windows App SDK compatible environment

### Development Notes

This is a packaged WinUI 3 desktop app. The recommended way to run and debug it is through **Visual Studio** using the packaged profile.

For runtime smoke tests, launch through package identity. Directly starting `bin\...\Kroira.App.exe` runs outside package identity and can fail during Windows App SDK bootstrap.

### Typical Development Flow

- open the solution in Visual Studio
- select the packaged startup profile
- build for `x64`
- run/debug from Visual Studio

You can also launch the packaged debug app with:

```powershell
.\scripts\launch-packaged-debug.ps1
```

## Project Direction

Short-term priorities:

- playback hardening and polish
- subtitle / audio track improvements
- aspect ratio / fit mode controls
- better playback status and error messaging
- longer session testing and stress testing

Long-term priorities:

- EPG improvements
- catalog polish
- localization improvements
- search / filter / sort improvements
- additional quality-of-life features

## Disclaimer

KROIRA IPTV is a client application only.

It does **not** include or distribute:
- channels
- playlists
- stream credentials
- copyrighted content

Users are responsible for the sources they add and use.

## License

Add your preferred license here.

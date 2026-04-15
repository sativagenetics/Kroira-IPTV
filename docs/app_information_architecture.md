# App Information Architecture

## 1. Onboarding & Initial States
- **First-Run Onboarding Flow**:
  - Welcome Screen (Clear "Bring your own content" legal disclaimer).
  - Import Wizard: Choose Source Type (Xtream / M3U / Local).
  - Credential Input / Setup form.
  - Sync Progress Screen.
- **No-Source Empty State**:
  - Central CTA: "Add your first media source".
  - Navigation menu effectively disabled (except Settings).

## 2. Navigation Structure Map (Root Layer)
Optimized for D-pad & Keyboard. The `NavigationView` supports the following root nodes:
1. **Live TV** (Channels, Categories).
2. **Catch-up / Archive** (Timeline scrolling for past broadcasts).
3. **Movies** (VOD grids, Search by genre).
4. **Series** (Seasons, Episodes).
5. **Continue Watching / Recent** (Dedicated hub for paused playback).
6. **Favorites** (Aggregated view of starred channels and VODs).
7. **Downloads & Recordings** (File management for active and completed local saves).
8. **Settings** (App Config).

## 3. Settings IA Tree
- `/Settings/Sources`: View, edit, sync, or delete active Source credentials. Access **Source Health / Diagnostics** (ping, HTTP trace).
- `/Settings/Playback`: External player handoff config, hardware decoding, default network caching, audio/subtitle preference. **Multi-Monitor Behavior** hooks.
- `/Settings/EPG`: EPG refresh interval, time shift, cache clearing.
- `/Settings/Recording`: Default directory for downloads/recordings.
- `/Settings/Parental`: Set PIN, define locked categories.
- `/Settings/Data`: **Backup/Export/Import** SQLite database settings.
- `/Settings/Licensing`: Current tier status, "Upgrade to Pro" button, view capabilities.

## 4. Error & Failure States
- **Source Refresh Failure**:
  - Discreet warning bar at top of UI. Retains old cached snapshot in DB so the user can still attempt playback.
- **Playback Error States**:
  - Overlay on video frame ("Stream Offline", "Codec Error").
  - "Diagnostics" button expands to show native libVLC failure code.
  - Auto-fallback timer returning to previous view after 10s.

## 5. Focus & Mapping Hooks
- Global `Ctrl+F` jumps to Search.
- Arrow keys strictly cycle UI focus; Left-bumper escapes to Nav Menu.
- Fullscreen hides pointer after 3000ms idle in `Playing` state.

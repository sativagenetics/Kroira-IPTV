# KROIRA IPTV 2.0.1 Manual QA Checklist

## Startup

- Launch the packaged app from a clean user profile.
- Confirm the shell appears quickly and does not wait for EPG refresh, source repair, or remote navigation setup.
- Confirm `%LOCALAPPDATA%\Kroira\diagnostics-log.txt` records `app_started` and `app_ready`.

## First Run

- Start with no configured IPTV source.
- Confirm the empty state says: "KROIRA does not provide content. Add your own IPTV source to start watching."
- Confirm source actions remain focused on user-provided sources: M3U, Xtream, and local file import.

## Source Import

- Import a large valid M3U playlist and confirm the UI remains responsive during parsing.
- Import an unreachable playlist URL and confirm a clear timeout or reachability error appears within the timeout window.
- Import an invalid M3U file and confirm the app reports "Invalid M3U format."
- Add Xtream credentials with a bad password and confirm the app reports "Xtream login failed."
- Cancel an in-progress source import and confirm the UI returns to a stable state.
- Confirm logs contain no full playlist URLs, query strings, usernames, passwords, or tokens.

## EPG

- Refresh EPG from Source Manager and EPG Center while navigating between Live, Movies, Series, and Settings.
- Confirm navigation remains responsive during EPG downloads and matching.
- Confirm EPG failures report "EPG could not be loaded." without corrupting existing guide data.

## Playback

- Start playback for a valid live stream and confirm diagnostics record `playback_started` and `playback_success`.
- Start playback for an invalid stream and confirm diagnostics record `playback_failed` and the UI shows a clear failure state.
- Rapidly switch channels ten or more times and confirm the UI stays responsive.
- Stop playback, navigate away, and close the app while playback is active.
- Confirm diagnostics record `player_dispose_started` and `player_dispose_completed`.

## Responsiveness Diagnostics

- During stress testing, confirm `ui_thread_block_warning` and `app_hang_detected` are written only when the UI thread is genuinely blocked.
- Confirm diagnostics are local-only and under `%LOCALAPPDATA%\Kroira`.


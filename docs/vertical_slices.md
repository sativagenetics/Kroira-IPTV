# Vertical Slices Implementation Roadmap

This roadmap completely dictates execution. Every slice must strictly meet acceptance criteria, undergo manual smoke tests, and be git-committed before proceeding. We do not skip ahead.

---

## Slice 1: Solution Scaffold & Licensing Foundation
- **Goal**: WinUI 3 structure with Dependency Injection. Establish Free/Pro entitlement abstractions early.
- **Paths**: `App.xaml.cs`, `Services/IEntitlementService.cs`, `Services/StoreEntitlementService.cs`
- **Acceptance Criteria**: App launches blank. DI provides a mock Pro license object to the UI.
- **Manual Smoke Test**: Run app, check debug log for "App Initialized - Validating License Cache".
- **Edge Cases**: Broken DI registration causing immediate target invocation crash.
- **Rollback Note**: Revert to empty skeleton.
- **Commit**: `chore: project scaffold and licensing foundation`

## Slice 2: Persistence & Domain
- **Goal**: EF Core SQLite schema implementation with `SchemaVersion` tracker.
- **Paths**: `Data/AppDbContext.cs`, `Models/SourceProfile.cs`, `Models/FeatureEntitlement.cs`
- **Acceptance Criteria**: App safely creates `.db` on first launch. Idempotent forward-only migration executes.
- **Manual Smoke Test**: Delete DB, run app, confirm DB recreation with tables. Verify the migration upgrade-validation hook completes.
- **Edge Cases**: Locked SQLite file due to rogue process.
- **Rollback Note**: `git clean -df` all EF migrations.
- **Commit**: `feat: domain models and ef core migrations`

## Slice 3: Store/Licensing UI
- **Goal**: Create Settings > Licensing Page linking to Store APIs.
- **Paths**: `Views/Settings/LicensingPage.xaml`, `ViewModels/LicensingViewModel.cs`
- **Acceptance Criteria**: User can navigate to Licensing tab and simulate 'Upgrade' click.
- **Manual Smoke Test**: Click "Upgrade", observe mock state change to unlocked.
- **Edge Cases**: Microsoft store DLLs missing in test environment.
- **Rollback Note**: Discard only `LicensingPage` files.
- **Commit**: `feat: licensing purchase flow UI`

## Slice 4: Fullscreen & Input State Machine
- **Goal**: Isolate Windowing & input hooking (F11, ESC, Pointer Hiding).
- **Paths**: `Core/WindowManager.cs`, `Core/InputInterceptor.cs`
- **Acceptance Criteria**: F11 maximizes window perfectly without title bars. ESC returns it.
- **Manual Smoke Test**: Drag window, hit F11. Move mouse around, ensure it targets active monitor correctly.
- **Edge Cases**: Mouse cursor gets trapped in off-screen coordinate.
- **Rollback Note**: Revert AppWindow modifications heavily.
- **Commit**: `feat: window fullscreen and input state machine`

## Slice 5: Playback Engine Abstraction
- **Goal**: Combine LibVLCSharp into `IPlaybackEngine` enforcing UI thread marshalling.
- **Paths**: `Services/Playback/IPlaybackEngine.cs`, `Services/Playback/LibVlcPlaybackEngine.cs`, `Extensions/DispatcherExtensions.cs`
- **Acceptance Criteria**: Video renders, UI properties change sequentially.
- **Manual Smoke Test**: Hardcode a dummy MP4. Verify visual render and slider tracking.
- **Edge Cases**: RPC_E_WRONG_THREAD crash when `MediaEnded` tries to change an `ObservableCollection` unmarshalled.
- **Rollback Note**: Drop `LibVLCSharp` Nuget temporarily.
- **Commit**: `feat: playback engine and thread marshalling`

## Slice 6: Source Management & Onboarding
- **Goal**: Xtream login screen and M3U parser UI with CancellationTokens.
- **Paths**: `Views/OnboardingPage.xaml`, `Services/M3uParserService.cs`
- **Acceptance Criteria**: User inputs valid creds, DB populates Channels into `SourceSyncState`.
- **Manual Smoke Test**: Ingest a hefty >10,000 line M3U. Cancel midway, ensure UI does not hang.
- **Rollback Note**: Hard revert on parser logic.
- **Commit**: `feat: source ingestion and parsing flow`

## Slice 7: Navigation & Player Shell Layer
- **Goal**: Left Navigation Menu + transparent OSD Overlay.
- **Paths**: `Views/ShellPage.xaml`, `Views/PlayerOverlay.xaml`
- **Acceptance Criteria**: Tab/arrow between "Live TV", "Settings". Player OSD triggers visibility changes properly.
- **Manual Smoke Test**: D-Pad navigate through the menu to ensure zero pointer-focus traps.
- **Commit**: `feat: main shell and UI navigation bounds`

## Slice 8: EPG Integration & Data Syncing
- **Goal**: Local SQLite EPG sync, timeline boundaries.
- **Paths**: `Services/EpgSyncService.cs`, `Views/Controls/EpgGrid.xaml`
- **Acceptance Criteria**: Parsed XMLTV accurately maps horizontally against active Channels.
- **Manual Smoke Test**: Verify "Now Playing" boundary updates dynamically as local time shifts past program ends.
- **Edge Cases**: Out of memory exception parsing 50MB XMLTV. Fix via streaming reader.
- **Rollback Note**: Revert Grid Control.
- **Commit**: `feat: epg data xmltv parsing and grid control`

## Slice 9: Favorites & Continue Watching
- **Goal**: Implement `PlaybackProgress` tracking logic upon stream stop. Add/Remove Favorites.
- **Paths**: `ViewModels/FavoritesViewModel.cs`, `Services/PlaybackSessionTracker.cs`
- **Acceptance Criteria**: Escaping a VOD at 10m:22s saves the state, allowing direct resume from that exact tick.
- **Manual Smoke Test**: Play VOD, pause, hit ESC. Go to "Continue Watching", click video, verify instant 10m:22s seek.
- **Commit**: `feat: favorites list and playback progress tracking`

## Slice 10: Multi-Monitor Hardening (Pro Area)
- **Goal**: Implement display context migration, PiP mini-player fallback.
- **Paths**: `Core/DisplayManager.cs`, `Views/MiniPlayerWindow.xaml`
- **Acceptance Criteria**: Hardware context safely suspends/resumes when window crosses varying DPI monitors.
- **Manual Smoke Test**: Drag playing 1080p stream from 4k monitor to 1080p monitor quickly. Assert LibVLC continues.
- **Commit**: `feat: multi-monitor dpi handoff and PiP mode`

## Slice 11: Recording & Download Pipeline (Pro Area)
- **Goal**: Offline media persistence.
- **Paths**: `Services/Offline/RecordingJobDispatcher.cs`, `Services/Offline/FileDownloader.cs`
- **Acceptance Criteria**: User uses `FolderPicker` to grant access. File writes safely taking `CancellationToken`.
- **Manual Smoke Test**: Start "Record", close player overlay. Wait 1 min, stop "Record". Open output `.ts` with VLC to verify.
- **Commit**: `feat: offline recording and download job dispatcher`

## Slice 12: Reliability & Polish Regression Pass
- **Goal**: Final sweeps for memory leaks, OSD hiding stability, error fallback tests.
- **Paths**: `Global`
- **Acceptance Criteria**: App passes all manual edge cases mapped in architecture (e.g. timeout on bad URL falls back safely to UI after 10s).
- **Manual Smoke Test**: Provide dead URL, ensure UI catches timeout natively without crashing.
- **Commit**: `chore: final usability regression passes`

## Slice 13: Packaging & Microsoft Store Submission Prep
- **Goal**: MSIX bundle generation and App Certification Kit checks.
- **Paths**: `Package.appxmanifest`
- **Acceptance Criteria**: MSBuild generates a valid `x64` and `ARM64` `.msixbundle`.
- **Manual Smoke Test**: Run local WACK (Windows App Certification Kit) against output bundle. Ensure 0 errors.
- **Commit**: `chore: store msix capability targeting`

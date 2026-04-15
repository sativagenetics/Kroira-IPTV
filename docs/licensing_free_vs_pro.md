# Licensing: Free vs Pro Tier

KROIRA's business strategy relies on offering a world-class foundational media player for free, whilst charging power users for complex library management and multi-display utility.

## 1. The "Never Paywalled" Core (Free Tier)
These core features are permanently accessible to all users without payment:
- **1 Source Profile Limit** (Unlimited channels and VODs within that single profile).
- **Core Playback Engine**: Flawless hardware-accelerated playback via LibVLC.
- **Basic EPG**: Present/Next timeline data.
- **Standard UI Navigation**: Full keyboard, remote, and mouse support.
- **Favorites & Search**: Normal search capabilities and starring.
- **External Player Fallback**: Handoff to external VLC/MPC-HC if internal codec fails. (Crucial for accessibility).
- **Basic Diagnostics**: Viewing stream HTTP error codes on playback failure.
- **Backup/Export Settings**: Ensuring users are never held hostage, they can backup their SQLite `.db` payload.

## 2. Pro Tier Feature Gates
Upgrading to Pro unlocks the following capabilities managed via an `IEntitlementService`:
- **Unlimited Multi-Source**: Adding a 2nd+ M3U or Xtream profile concurrently.
- **Deep EPG Sync & Catch-up**: Multi-day EPG retention and Catch-up (Archive) timeline grid scrolling.
- **Multi-Monitor & Windowing Tools**: Picture-in-Picture (PiP) mini-player overlay and advanced multi-screen targeting strategies.
- **Recording & Downloading**: The entire offline pipeline. Scheduling Live TV to record to disk, or saving VODs directly.
- **Advanced Source Health Tools**: Deep DNS tracing, mass channel health pings, and connection diagnostic suites.
- **Parental Controls**: Requiring a PIN to access specific UI categories or Sources.

## 3. Offline Validation & Entitlement Cache
- Store purchases communicate via `Windows.Services.Store` API.
- The `FeatureEntitlement` table in SQLite caches the verified status alongside a cryptographically signed signature.
- If launched completely offline without Store connection, the app trusts the local SQLite cache for 30 days to avoid disruption.

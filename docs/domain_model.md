# Domain Model & Persistence Rules

## 1. Safety Constraint: Zero Data Loss
User data must NEVER be silently dropped during migration, app crash, or source sync failure.

## 2. Core Entities & State Tracking

### 2.1 Playback State & Progress (Expanded)
- `PlaybackProgress`: Distinct from a "Recent" list. Stores real-time playhead positions.
  - Granular tracking per media type.
  - Movies: Save milliseconds watched. Resume implies jumping strictly to that point.
  - Episodes: Track progression (e.g., Season 1, Ep 4 at 12m:30s).
  - Partial states: Marked as "Watched" automatically if >90% complete.
- `ContinueWatching`: An aggregation query / logical view tying `UserProfile` to recently active `PlaybackProgress` records.
- `Favorite`: Link to a specific `Channel` or `Movie` ID. Survives source syncing via stable external IDs.

### 2.2 Infrastructure & Security
- `AppSetting`: Key-Value table for local UI configurations.
- `FeatureEntitlement`: Cache of verified store capabilities (Pro flags).
- `ParentalControlSetting`: Encrypted PIN hash and globally locked category IDs.
- `SchemaVersion`: Logs applied migrations.

### 2.3 Media & Source Layout
- `SourceProfile`: Container for a user's subscription (e.g., "My IPTV 1").
- `SourceCredential`: Secure storage of URLs or Xtream Logins.
- `SourceSyncState`: Timestamps and HTTP status codes of the last update.
- `ChannelCategory` / `Channel` / `EpgProgram`: Live stream nodes and time-bounded airings.
- `Movie` / `Series` / `Season` / `Episode`: VOD representation, TMDB metadata.
- `RecordingJob` / `DownloadJob`: Definitions and progress of local offline saves.

## 3. Migration & Upgrade-Path Validation Strategy
All Entity Framework migrations must be idempotent and forward-only.
- **Upgrade-Path Validation**: Before `context.Database.Migrate()` runs, the boot service must explicitly read the `SchemaVersion` table. We enforce snapshot testing on app startup. If the existing schema violates the expected upgrade path (e.g., an unauthorized downgrade or corrupted structure is detected), the migration must **halt immediately** rather than tearing down tables.
- A backup `.db` copy is created automatically before any schema alter scripts run.

## 4. Design Targeting
The domain is conceptually abstracted so entities aren't hard-locked to the UI thread, but the V1 scope relies solely on local Windows desktop playback.

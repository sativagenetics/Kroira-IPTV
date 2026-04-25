# Domain Model And Persistence Rules

This document describes the current V2 persistence model at a product level. It is intentionally conservative: user data must survive source sync failures, app crashes, and release upgrades.

## 1. Safety Constraint: Zero Silent Data Loss

User data must never be silently dropped during migration, source sync, app startup repair, or crash recovery.

- Prefer additive schema changes and forward-compatible backfills.
- Create local database backups before destructive migration steps.
- Halt unsafe migration paths instead of dropping tables or rebuilding from scratch.
- Preserve existing plaintext compatibility fields until replacement flows are fully productized and validated.

## 2. Source And Credential State

- `SourceProfile`: User-visible source container.
- `SourceCredential`: Source URL, source type, guide mode, Xtream credentials, M3U settings, portal profile fields, and detected/fallback guide URLs.
- `SourceProtectedCredentialSecret`: Protected credential copies using Windows DPAPI CurrentUser scope when available.
- `SourceSyncState`: Last import/sync timestamps, status, and failure context.
- `SourceAcquisitionProfile` / `SourceAcquisitionRun`: Import profile and run history for source ingestion.
- `SourceHealthReport`, `SourceHealthComponent`, `SourceHealthProbe`, `SourceHealthIssue`: Source diagnostics, health scoring, bounded probe results, and repair signals.
- `SourceActivityRecord`: Safe-to-share operational summaries for source activity.
- `StalkerPortalSnapshot`: Provider portal discovery/profile summary for supported portal sources.

## 3. Catalog State

- `ChannelCategory` / `Channel`: Live TV catalog, provider categories, EPG identity, logo state, catch-up signals, and source ownership.
- `Movie`: VOD movie catalog entries, provider metadata, normalized identity, artwork, and TMDb enrichment fields.
- `Series` / `Season` / `Episode`: VOD series catalog entries, season/episode layout, provider metadata, and TMDb enrichment fields.
- Logical catalog and operational state services keep duplicate provider entries, mirror candidates, and fallback playback resolution understandable without deleting the underlying source data.

## 4. EPG And Metadata State

- `EpgProgram`: XMLTV programmes attached to live channels.
- `EpgSyncLog`: Guide sync status, source mode, coverage counts, warning summaries, and XMLTV source status.
- `EpgMappingDecision`: Manual and reviewed EPG match decisions.
- TMDb metadata is optional enrichment/fallback. It improves artwork and metadata when configured, but it does not provide stream access or content rights.

## 5. Personal App State

- `AppProfile`: Local app profile identity.
- `AppSetting`: Local preferences and feature settings.
- `Favorite`: Profile-aware saved live channels, movies, and series.
- `PlaybackProgress`: Profile-aware playhead positions, watched state, and VOD resume data.
- `ContinueWatching`: Logical view over recent playback progress.
- `ParentalControlSetting`: Local access-control state where enabled.

## 6. Present But Release-Gated Areas

The codebase contains persistence models and services for:

- `RecordingJob`
- `DownloadJob`
- `FeatureEntitlement`
- media-library restore/export related state

These areas are not part of the default public V2 RC surface unless their feature gates are intentionally opened, validated, documented, and represented in Store copy.

## 7. Migration And Upgrade Validation

All Entity Framework migrations and bootstrap repairs must be idempotent where practical and forward-only.

- Startup repair should add missing columns/tables needed by V2 without destroying user data.
- Upgrade validation should detect corrupted or incompatible schemas and stop safely.
- Regression coverage should protect migration, parser, source health, EPG, and playback-resolution behavior.
- Any migration that changes user-visible data must include a test or regression case when practical.

## 8. Release Target

The V2 target is a local Windows desktop player and source manager. KROIRA does not provide IPTV content, provider accounts, playlists, credentials, subscriptions, or media rights.

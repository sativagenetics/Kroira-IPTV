using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Data
{
    public static class DatabaseBootstrapper
    {
        public static string RuntimeDatabasePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "kroira.db");

        public static void Initialize(AppDbContext context)
        {
            var dbFolder = Path.GetDirectoryName(RuntimeDatabasePath)!;
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }

            string dbPath = RuntimeDatabasePath;

            if (File.Exists(dbPath))
            {
                CreateRuntimeBackupIfNeeded(dbPath);

                // Real upgrade-path validation before EF migration boots
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SchemaVersions';";
                bool tableExists = cmd.ExecuteScalar() != null;

                if (tableExists)
                {
                    cmd.CommandText = "SELECT VersionNumber FROM SchemaVersions ORDER BY AppliedAt DESC LIMIT 1;";
                    var result = cmd.ExecuteScalar();
                    if (result != null && int.TryParse(result.ToString(), out int currentDbVersion))
                    {
                        int targetAppVersion = 1; // Explicit V1 boundary lock
                        if (currentDbVersion > targetAppVersion)
                        {
                            throw new InvalidOperationException($"FATAL HALT: Current Database Schema Version ({currentDbVersion}) is from a newer application version. Downgrading is prohibited to prevent data loss.");
                        }
                    }
                }
            }

            try
            {
                context.Database.Migrate();
                EnsureRuntimeSchemaCore(dbPath);
            }
            catch (Exception ex)
            {
                if (IsRecoverableMigrationDrift(ex))
                {
                    EnsureRuntimeSchemaCore(dbPath);
                    return;
                }

                throw new InvalidOperationException(
                    $"Failed to migrate database securely. App initialization halted. Error: {ex.Message}",
                    ex);
            }
        }

        public static void EnsureRuntimeSchema(AppDbContext context)
        {
            var dbPath = context.Database.GetDbConnection().DataSource;
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                throw new InvalidOperationException("Unable to resolve the current SQLite database path.");
            }

            EnsureRuntimeSchemaCore(dbPath);
        }

        private static void EnsureRuntimeSchemaCore(string dbPath)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            EnsureAppProfilesTable(conn);
            EnsureDefaultProfile(conn);
            EnsureActiveProfileSetting(conn);

            EnsureColumn(conn, "Favorites", "ProfileId", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "Favorites", "LogicalContentKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Favorites", "PreferredSourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Favorites", "ResolvedAtUtc", "TEXT");
            EnsureColumn(conn, "PlaybackProgresses", "ProfileId", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "PlaybackProgresses", "LogicalContentKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "PlaybackProgresses", "PreferredSourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "PlaybackProgresses", "DurationMs", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "PlaybackProgresses", "WatchStateOverride", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "PlaybackProgresses", "CompletedAtUtc", "TEXT");
            EnsureColumn(conn, "PlaybackProgresses", "ResolvedAtUtc", "TEXT");
            EnsureColumn(conn, "ParentalControlSettings", "ProfileId", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "ParentalControlSettings", "LockedSourceIdsJson", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "ParentalControlSettings", "IsKidsSafeMode", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "ParentalControlSettings", "HideLockedContent", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "RecordingJobs", "ProfileId", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "RecordingJobs", "ChannelName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "RecordingJobs", "StreamUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "RecordingJobs", "RequestedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "RecordingJobs", "StartedAtUtc", "TEXT");
            EnsureColumn(conn, "RecordingJobs", "CompletedAtUtc", "TEXT");
            EnsureColumn(conn, "RecordingJobs", "NextRetryAtUtc", "TEXT");
            EnsureColumn(conn, "RecordingJobs", "UpdatedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "RecordingJobs", "TempOutputPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "RecordingJobs", "FileName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "RecordingJobs", "FileSizeBytes", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "RecordingJobs", "RetryCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "RecordingJobs", "MaxRetryCount", "INTEGER NOT NULL DEFAULT 2");
            EnsureColumn(conn, "RecordingJobs", "LastError", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "DownloadJobs", "ProfileId", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "DownloadJobs", "Title", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "DownloadJobs", "Subtitle", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "DownloadJobs", "StreamUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "DownloadJobs", "RequestedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "DownloadJobs", "StartedAtUtc", "TEXT");
            EnsureColumn(conn, "DownloadJobs", "CompletedAtUtc", "TEXT");
            EnsureColumn(conn, "DownloadJobs", "NextRetryAtUtc", "TEXT");
            EnsureColumn(conn, "DownloadJobs", "UpdatedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "DownloadJobs", "TempOutputPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "DownloadJobs", "FileName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "DownloadJobs", "FileSizeBytes", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "DownloadJobs", "RetryCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "DownloadJobs", "MaxRetryCount", "INTEGER NOT NULL DEFAULT 2");
            EnsureColumn(conn, "DownloadJobs", "LastError", "TEXT NOT NULL DEFAULT ''");

            EnsureColumn(conn, "Movies", "BackdropUrl", "TEXT");
            EnsureColumn(conn, "Movies", "Genres", "TEXT");
            EnsureColumn(conn, "Movies", "ImdbId", "TEXT");
            EnsureColumn(conn, "Movies", "MetadataUpdatedAt", "TEXT");
            EnsureColumn(conn, "Movies", "OriginalLanguage", "TEXT");
            EnsureColumn(conn, "Movies", "Overview", "TEXT");
            EnsureColumn(conn, "Movies", "CanonicalTitleKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Movies", "DedupFingerprint", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Movies", "RawSourceCategoryName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Movies", "RawSourceTitle", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Movies", "ContentKind", "TEXT NOT NULL DEFAULT 'Primary'");
            EnsureColumn(conn, "Movies", "Popularity", "REAL NOT NULL DEFAULT 0.0");
            EnsureColumn(conn, "Movies", "ReleaseDate", "TEXT");
            EnsureColumn(conn, "Movies", "TmdbBackdropPath", "TEXT");
            EnsureColumn(conn, "Movies", "TmdbPosterPath", "TEXT");
            EnsureColumn(conn, "Movies", "VoteAverage", "REAL NOT NULL DEFAULT 0.0");

            EnsureColumn(conn, "Series", "BackdropUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "FirstAirDate", "TEXT");
            EnsureColumn(conn, "Series", "Genres", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "ImdbId", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "MetadataUpdatedAt", "TEXT");
            EnsureColumn(conn, "Series", "OriginalLanguage", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "Overview", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "CanonicalTitleKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "DedupFingerprint", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "RawSourceCategoryName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "RawSourceTitle", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "ContentKind", "TEXT NOT NULL DEFAULT 'Primary'");
            EnsureColumn(conn, "Series", "Popularity", "REAL NOT NULL DEFAULT 0.0");
            EnsureColumn(conn, "Series", "TmdbBackdropPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "TmdbPosterPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "VoteAverage", "REAL NOT NULL DEFAULT 0.0");

            // EPG pass: nullable programme metadata columns
            EnsureColumn(conn, "Channels", "EpgChannelId", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "ProviderLogoUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "ProviderEpgChannelId", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "NormalizedIdentityKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "NormalizedName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "AliasKeys", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "EpgMatchSource", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Channels", "EpgMatchConfidence", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Channels", "EpgMatchSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "LogoSource", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Channels", "LogoConfidence", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Channels", "LogoSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "ProviderCatchupMode", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "ProviderCatchupSource", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "SupportsCatchup", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Channels", "CatchupWindowHours", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Channels", "CatchupSource", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Channels", "CatchupConfidence", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "Channels", "CatchupSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Channels", "CatchupDetectedAtUtc", "TEXT");
            EnsureColumn(conn, "Channels", "EnrichedAtUtc", "TEXT");
            EnsureColumn(conn, "EpgPrograms", "Subtitle", "TEXT");
            EnsureColumn(conn, "EpgPrograms", "Category", "TEXT");
            EnsureColumn(conn, "SourceCredentials", "DetectedEpgUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceCredentials", "EpgMode", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "SourceCredentials", "ProxyScope", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceCredentials", "ProxyUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceSyncStates", "LastAutoRefreshAttemptAtUtc", "TEXT");
            EnsureColumn(conn, "SourceSyncStates", "LastAutoRefreshSuccessAtUtc", "TEXT");
            EnsureColumn(conn, "SourceSyncStates", "NextAutoRefreshDueAtUtc", "TEXT");
            EnsureColumn(conn, "SourceSyncStates", "AutoRefreshState", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceSyncStates", "AutoRefreshSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceSyncStates", "AutoRefreshFailureCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureSourceAcquisitionTables(conn);
            EnsureColumn(conn, "SourceAcquisitionProfiles", "SourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "ProfileKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "ProfileLabel", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "ProviderKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "NormalizationSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "MatchingSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "SuppressionSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "ValidationSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "SupportsRegexMatching", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "PreferProxyDuringValidation", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "PreferLastKnownGoodRollback", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionProfiles", "UpdatedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "SourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "Trigger", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "Scope", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "Status", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "StartedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "CompletedAtUtc", "TEXT");
            EnsureColumn(conn, "SourceAcquisitionRuns", "ProfileKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "ProfileLabel", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "ProviderKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "RoutingSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "ValidationRoutingSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "Message", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "CatalogSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "GuideSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "ValidationSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionRuns", "RawItemCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "AcceptedCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "SuppressedCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "DemotedCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "MatchedCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "UnmatchedCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "LiveCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "MovieCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "SeriesCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "EpisodeCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "AliasMatchCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "RegexMatchCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "FuzzyMatchCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "ProbeSuccessCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "ProbeFailureCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "WarningCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionRuns", "ErrorCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "SourceAcquisitionRunId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "SourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "Stage", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "Outcome", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "ItemKind", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "RuleCode", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "Reason", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "RawName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "RawCategory", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "NormalizedName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "NormalizedCategory", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "IdentityKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "AliasKeys", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "MatchedValue", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "MatchedTarget", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "Confidence", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceAcquisitionEvidence", "SortOrder", "INTEGER NOT NULL DEFAULT 0");

            // M3U import mode for SourceCredentials.
            //  • New-column default = 2 (LiveMoviesAndSeries) — matches the
            //    current model default.
            //  • Existing rows that still have the legacy default of 1
            //    (LiveAndMovies, set by the original AddM3uImportMode
            //    migration) are bumped to 2 so M3U sources import series
            //    without requiring a manual mode change. Value 0 (LiveOnly)
            //    is preserved because it represents an explicit user choice.
            EnsureColumn(conn, "SourceCredentials", "M3uImportMode", "INTEGER NOT NULL DEFAULT 2");
            BumpLegacyM3uImportMode(conn);
            BackfillLegacyEpgMode(conn);

            EnsureSourceHealthTables(conn);
            EnsureColumn(conn, "SourceHealthReports", "EvaluatedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthReports", "LastSyncAttemptAtUtc", "TEXT");
            EnsureColumn(conn, "SourceHealthReports", "LastSuccessfulSyncAtUtc", "TEXT");
            EnsureColumn(conn, "SourceHealthReports", "HealthScore", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "HealthState", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "StatusSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthReports", "ImportResultSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthReports", "ValidationSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthReports", "TopIssueSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthReports", "TotalChannelCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "TotalMovieCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "TotalSeriesCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "DuplicateCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "InvalidStreamCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "ChannelsWithEpgMatchCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "ChannelsWithCurrentProgramCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "ChannelsWithNextProgramCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "ChannelsWithLogoCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "SuspiciousEntryCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "WarningCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthReports", "ErrorCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthIssues", "Severity", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthIssues", "Code", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthIssues", "Title", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthIssues", "Message", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthIssues", "AffectedCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthIssues", "SampleItems", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthIssues", "SortOrder", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthComponents", "SourceHealthReportId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthComponents", "ComponentType", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthComponents", "State", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthComponents", "Score", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthComponents", "Summary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthComponents", "RelevantCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthComponents", "HealthyCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthComponents", "IssueCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthComponents", "SortOrder", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "SourceHealthReportId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "ProbeType", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "Status", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "ProbedAtUtc", "TEXT");
            EnsureColumn(conn, "SourceHealthProbes", "CandidateCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "SampleSize", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "SuccessCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "FailureCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "TimeoutCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "HttpErrorCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "TransportErrorCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceHealthProbes", "Summary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceHealthProbes", "SortOrder", "INTEGER NOT NULL DEFAULT 0");

            EnsureSourceEnrichmentTables(conn);
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "SourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "IdentityKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "NormalizedName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "AliasKeys", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "ProviderName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "ProviderEpgChannelId", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "ProviderLogoUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "ResolvedLogoUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "MatchedXmltvChannelId", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "MatchedXmltvDisplayName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "MatchedXmltvIconUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "EpgMatchSource", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "EpgMatchConfidence", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "EpgMatchSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "LogoSource", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "LogoConfidence", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "LogoSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "LastAppliedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "SourceChannelEnrichmentRecords", "LastSeenAtUtc", "TEXT NOT NULL DEFAULT ''");

            EnsureOperationalTables(conn);
            EnsureColumn(conn, "LogicalOperationalStates", "ContentType", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "LogicalContentKey", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "LogicalOperationalStates", "CandidateCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "PreferredContentId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "PreferredSourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "PreferredScore", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "SelectionSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "LogicalOperationalStates", "LastKnownGoodContentId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "LastKnownGoodSourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "LastKnownGoodScore", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "LastKnownGoodAtUtc", "TEXT");
            EnsureColumn(conn, "LogicalOperationalStates", "LastPlaybackSuccessAtUtc", "TEXT");
            EnsureColumn(conn, "LogicalOperationalStates", "LastPlaybackFailureAtUtc", "TEXT");
            EnsureColumn(conn, "LogicalOperationalStates", "ConsecutivePlaybackFailures", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "RecoveryAction", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalStates", "RecoverySummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "LogicalOperationalStates", "SnapshotEvaluatedAtUtc", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "LogicalOperationalStates", "PreferredUpdatedAtUtc", "TEXT");

            EnsureColumn(conn, "LogicalOperationalCandidates", "LogicalOperationalStateId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalCandidates", "ContentId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalCandidates", "SourceProfileId", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalCandidates", "Rank", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalCandidates", "Score", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalCandidates", "IsSelected", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalCandidates", "IsLastKnownGood", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalCandidates", "SupportsProxy", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "LogicalOperationalCandidates", "SourceName", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "LogicalOperationalCandidates", "StreamUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "LogicalOperationalCandidates", "Summary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "LogicalOperationalCandidates", "LastSeenAtUtc", "TEXT NOT NULL DEFAULT ''");

            // EPG pass: per-source sync health log (CREATE TABLE IF NOT EXISTS is safe to repeat)
            EnsureEpgSyncLogsTable(conn);
            EnsureColumn(conn, "EpgSyncLogs", "LastSuccessAtUtc", "TEXT");
            EnsureColumn(conn, "EpgSyncLogs", "Status", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "EpgSyncLogs", "ResultCode", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "EpgSyncLogs", "FailureStage", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "EpgSyncLogs", "ActiveMode", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "EpgSyncLogs", "ActiveXmltvUrl", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "EpgSyncLogs", "UnmatchedChannelCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "EpgSyncLogs", "CurrentCoverageCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "EpgSyncLogs", "NextCoverageCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "EpgSyncLogs", "TotalLiveChannelCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "EpgSyncLogs", "MatchBreakdown", "TEXT NOT NULL DEFAULT ''");
            BackfillLegacyEpgSyncLogColumns(conn);

            EnsureIndex(conn, "IX_Movies_MetadataUpdatedAt", "Movies", "MetadataUpdatedAt");
            EnsureIndex(conn, "IX_Movies_CanonicalTitleKey", "Movies", "CanonicalTitleKey");
            EnsureIndex(conn, "IX_Movies_DedupFingerprint", "Movies", "DedupFingerprint");
            EnsureIndex(conn, "IX_Movies_TmdbId", "Movies", "TmdbId");
            EnsureIndex(conn, "IX_AppProfiles_Name", "AppProfiles", "Name");
            EnsureIndex(conn, "IX_Series_MetadataUpdatedAt", "Series", "MetadataUpdatedAt");
            EnsureIndex(conn, "IX_Series_CanonicalTitleKey", "Series", "CanonicalTitleKey");
            EnsureIndex(conn, "IX_Series_DedupFingerprint", "Series", "DedupFingerprint");
            EnsureIndex(conn, "IX_Series_TmdbId", "Series", "TmdbId");
            EnsureIndex(conn, "IX_Channels_EpgChannelId", "Channels", "EpgChannelId");
            EnsureIndex(conn, "IX_Channels_NormalizedIdentityKey", "Channels", "NormalizedIdentityKey");
            EnsureIndex(conn, "IX_SourceAcquisitionRuns_SourceProfileId", "SourceAcquisitionRuns", "SourceProfileId");
            EnsureIndex(conn, "IX_SourceAcquisitionEvidence_SourceAcquisitionRunId", "SourceAcquisitionEvidence", "SourceAcquisitionRunId");
            EnsureIndex(conn, "IX_SourceAcquisitionEvidence_SourceProfileId", "SourceAcquisitionEvidence", "SourceProfileId");
            EnsureIndex(conn, "IX_SourceAcquisitionProfiles_SourceProfileId", "SourceAcquisitionProfiles", "SourceProfileId", unique: true);
            EnsureCompositeIndex(conn, "IX_SourceAcquisitionRuns_SourceProfileId_StartedAtUtc", "SourceAcquisitionRuns", "SourceProfileId", "StartedAtUtc");
            EnsureCompositeIndex(conn, "IX_SourceAcquisitionEvidence_SourceAcquisitionRunId_SortOrder", "SourceAcquisitionEvidence", "SourceAcquisitionRunId", "SortOrder");
            EnsureIndex(conn, "IX_SourceHealthReports_SourceProfileId", "SourceHealthReports", "SourceProfileId", unique: true);
            EnsureIndex(conn, "IX_SourceHealthIssues_SourceHealthReportId", "SourceHealthIssues", "SourceHealthReportId");
            EnsureIndex(conn, "IX_SourceHealthComponents_SourceHealthReportId", "SourceHealthComponents", "SourceHealthReportId");
            EnsureUniqueCompositeIndex(conn, "IX_SourceHealthComponents_SourceHealthReportId_ComponentType", "SourceHealthComponents", "SourceHealthReportId", "ComponentType");
            EnsureIndex(conn, "IX_SourceHealthProbes_SourceHealthReportId", "SourceHealthProbes", "SourceHealthReportId");
            EnsureUniqueCompositeIndex(conn, "IX_SourceHealthProbes_SourceHealthReportId_ProbeType", "SourceHealthProbes", "SourceHealthReportId", "ProbeType");
            EnsureIndex(conn, "IX_SourceChannelEnrichmentRecords_SourceProfileId", "SourceChannelEnrichmentRecords", "SourceProfileId");
            EnsureUniqueCompositeIndex(conn, "IX_SourceChannelEnrichmentRecords_SourceProfileId_IdentityKey", "SourceChannelEnrichmentRecords", "SourceProfileId", "IdentityKey");
            EnsureUniqueCompositeIndex(conn, "IX_LogicalOperationalStates_ContentType_LogicalContentKey", "LogicalOperationalStates", "ContentType", "LogicalContentKey");
            EnsureCompositeIndex(conn, "IX_LogicalOperationalCandidates_SourceProfileId_IsSelected", "LogicalOperationalCandidates", "SourceProfileId", "IsSelected");
            EnsureTripleCompositeIndex(conn, "IX_LogicalOperationalCandidates_LogicalOperationalStateId_ContentId_SourceProfileId", "LogicalOperationalCandidates", "LogicalOperationalStateId", "ContentId", "SourceProfileId");
            EnsureIndex(conn, "IX_ParentalControlSettings_ProfileId", "ParentalControlSettings", "ProfileId", unique: true);
            EnsureTripleCompositeIndex(conn, "IX_Favorites_ProfileId_ContentType_ContentId", "Favorites", "ProfileId", "ContentType", "ContentId");
            EnsureTripleCompositeIndex(conn, "IX_Favorites_ProfileId_ContentType_LogicalContentKey", "Favorites", "ProfileId", "ContentType", "LogicalContentKey");
            EnsureTripleCompositeIndex(conn, "IX_PlaybackProgresses_ProfileId_ContentType_ContentId", "PlaybackProgresses", "ProfileId", "ContentType", "ContentId");
            EnsureTripleCompositeIndex(conn, "IX_PlaybackProgresses_ProfileId_ContentType_LogicalContentKey", "PlaybackProgresses", "ProfileId", "ContentType", "LogicalContentKey");
            EnsureTripleCompositeIndex(conn, "IX_RecordingJobs_ProfileId_Status_StartTimeUtc", "RecordingJobs", "ProfileId", "Status", "StartTimeUtc");
            EnsureTripleCompositeIndex(conn, "IX_DownloadJobs_ProfileId_Status_RequestedAtUtc", "DownloadJobs", "ProfileId", "Status", "RequestedAtUtc");
            EnsureCompositeIndex(conn, "IX_EpgPrograms_ChannelId_StartTimeUtc", "EpgPrograms", "ChannelId", "StartTimeUtc");
            EnsureIndex(conn, "IX_SourceSyncStates_NextAutoRefreshDueAtUtc", "SourceSyncStates", "NextAutoRefreshDueAtUtc");

            BackfillLegacyProfileState(conn);
        }

        private static void CreateRuntimeBackupIfNeeded(string dbPath)
        {
            var stamp = File.GetLastWriteTimeUtc(dbPath).ToString("yyyyMMddHHmmss");
            var backupPath = $"{dbPath}.{stamp}.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(dbPath, backupPath, overwrite: false);
            }
        }

        private static void BumpLegacyM3uImportMode(SqliteConnection conn)
        {
            if (!TableExists(conn, "SourceCredentials")) return;
            if (!ColumnExists(conn, "SourceCredentials", "M3uImportMode")) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE \"SourceCredentials\" SET \"M3uImportMode\" = 2 WHERE \"M3uImportMode\" = 1;";
            cmd.ExecuteNonQuery();
        }

        private static void BackfillLegacyEpgMode(SqliteConnection conn)
        {
            if (!TableExists(conn, "SourceCredentials")) return;
            if (!ColumnExists(conn, "SourceCredentials", "EpgMode")) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ""SourceCredentials""
                SET ""EpgMode"" = CASE
                    WHEN TRIM(COALESCE(""EpgUrl"", '')) <> '' THEN 2
                    WHEN ""EpgMode"" IS NULL OR ""EpgMode"" = 0 THEN 1
                    ELSE ""EpgMode""
                END;";
            cmd.ExecuteNonQuery();
        }

        private static void BackfillLegacyEpgSyncLogColumns(SqliteConnection conn)
        {
            if (!TableExists(conn, "EpgSyncLogs"))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ""EpgSyncLogs""
                SET ""LastSuccessAtUtc"" = COALESCE(""LastSuccessAtUtc"", CASE WHEN ""IsSuccess"" = 1 THEN ""SyncedAtUtc"" ELSE NULL END),
                    ""Status"" = CASE
                        WHEN ""Status"" <> 0 THEN ""Status""
                        WHEN ""IsSuccess"" = 1 THEN 2
                        WHEN COALESCE(""FailureReason"", '') LIKE '%does not advertise an XMLTV guide URL%' THEN 3
                        ELSE 4
                    END,
                    ""ResultCode"" = CASE
                        WHEN ""ResultCode"" <> 0 THEN ""ResultCode""
                        WHEN ""IsSuccess"" = 1 THEN 1
                        WHEN COALESCE(""FailureReason"", '') LIKE '%does not advertise an XMLTV guide URL%' THEN 4
                        ELSE 5
                    END,
                    ""FailureStage"" = CASE
                        WHEN ""FailureStage"" <> 0 THEN ""FailureStage""
                        WHEN ""IsSuccess"" = 1 THEN 0
                        WHEN COALESCE(""FailureReason"", '') LIKE '%XMLTV%' THEN 2
                        ELSE 2
                    END,
                    ""ActiveMode"" = CASE
                        WHEN ""ActiveMode"" <> 0 THEN ""ActiveMode""
                        ELSE 1
                    END
                WHERE 1 = 1;";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureAppProfilesTable(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""AppProfiles"" (
                    ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""Name""         TEXT    NOT NULL DEFAULT '',
                    ""IsKidsProfile"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAtUtc"" TEXT    NOT NULL DEFAULT ''
                );";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureDefaultProfile(SqliteConnection conn)
        {
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(1) FROM \"AppProfiles\";";
            var count = Convert.ToInt32(countCmd.ExecuteScalar());
            if (count > 0)
            {
                return;
            }

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO ""AppProfiles"" (""Id"", ""Name"", ""IsKidsProfile"", ""CreatedAtUtc"")
                VALUES (1, 'Primary', 0, $createdAtUtc);";
            insertCmd.Parameters.AddWithValue("$createdAtUtc", DateTime.UtcNow.ToString("o"));
            insertCmd.ExecuteNonQuery();
        }

        private static void EnsureActiveProfileSetting(SqliteConnection conn)
        {
            if (!TableExists(conn, "AppSettings"))
            {
                return;
            }

            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT 1 FROM \"AppSettings\" WHERE \"Key\" = 'Profiles.ActiveProfileId' LIMIT 1;";
            if (checkCmd.ExecuteScalar() != null)
            {
                return;
            }

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO ""AppSettings"" (""Key"", ""Value"")
                VALUES ('Profiles.ActiveProfileId', '1');";
            insertCmd.ExecuteNonQuery();
        }

        private static void BackfillLegacyProfileState(SqliteConnection conn)
        {
            if (TableExists(conn, "Favorites") && ColumnExists(conn, "Favorites", "ProfileId"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE \"Favorites\" SET \"ProfileId\" = 1 WHERE \"ProfileId\" IS NULL OR \"ProfileId\" <= 0;";
                cmd.ExecuteNonQuery();
            }

            if (TableExists(conn, "PlaybackProgresses") && ColumnExists(conn, "PlaybackProgresses", "ProfileId"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE \"PlaybackProgresses\" SET \"ProfileId\" = 1 WHERE \"ProfileId\" IS NULL OR \"ProfileId\" <= 0;";
                cmd.ExecuteNonQuery();
            }

            if (!TableExists(conn, "ParentalControlSettings"))
            {
                return;
            }

            if (ColumnExists(conn, "ParentalControlSettings", "ProfileId"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE \"ParentalControlSettings\" SET \"ProfileId\" = 1 WHERE \"ProfileId\" IS NULL OR \"ProfileId\" <= 0;";
                cmd.ExecuteNonQuery();
            }

            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(1) FROM \"ParentalControlSettings\";";
            var count = Convert.ToInt32(countCmd.ExecuteScalar());
            if (count > 0)
            {
                return;
            }

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO ""ParentalControlSettings""
                    (""ProfileId"", ""PinHash"", ""LockedCategoryIdsJson"", ""LockedSourceIdsJson"", ""IsKidsSafeMode"", ""HideLockedContent"")
                VALUES
                    (1, '', '', '', 0, 1);";
            insertCmd.ExecuteNonQuery();
        }

        private static void EnsureSourceAcquisitionTables(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""SourceAcquisitionProfiles"" (
                    ""Id""                          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceProfileId""             INTEGER NOT NULL,
                    ""ProfileKey""                  TEXT    NOT NULL DEFAULT '',
                    ""ProfileLabel""                TEXT    NOT NULL DEFAULT '',
                    ""ProviderKey""                 TEXT    NOT NULL DEFAULT '',
                    ""NormalizationSummary""        TEXT    NOT NULL DEFAULT '',
                    ""MatchingSummary""             TEXT    NOT NULL DEFAULT '',
                    ""SuppressionSummary""          TEXT    NOT NULL DEFAULT '',
                    ""ValidationSummary""           TEXT    NOT NULL DEFAULT '',
                    ""SupportsRegexMatching""       INTEGER NOT NULL DEFAULT 0,
                    ""PreferProxyDuringValidation"" INTEGER NOT NULL DEFAULT 0,
                    ""PreferLastKnownGoodRollback"" INTEGER NOT NULL DEFAULT 0,
                    ""UpdatedAtUtc""                TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT ""FK_SourceAcquisitionProfiles_SourceProfiles_SourceProfileId""
                        FOREIGN KEY (""SourceProfileId"")
                        REFERENCES ""SourceProfiles"" (""Id"")
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ""SourceAcquisitionRuns"" (
                    ""Id""                       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceProfileId""          INTEGER NOT NULL,
                    ""Trigger""                  INTEGER NOT NULL DEFAULT 0,
                    ""Scope""                    INTEGER NOT NULL DEFAULT 0,
                    ""Status""                   INTEGER NOT NULL DEFAULT 0,
                    ""StartedAtUtc""             TEXT    NOT NULL DEFAULT '',
                    ""CompletedAtUtc""           TEXT,
                    ""ProfileKey""               TEXT    NOT NULL DEFAULT '',
                    ""ProfileLabel""             TEXT    NOT NULL DEFAULT '',
                    ""ProviderKey""              TEXT    NOT NULL DEFAULT '',
                    ""RoutingSummary""           TEXT    NOT NULL DEFAULT '',
                    ""ValidationRoutingSummary"" TEXT    NOT NULL DEFAULT '',
                    ""Message""                  TEXT    NOT NULL DEFAULT '',
                    ""CatalogSummary""           TEXT    NOT NULL DEFAULT '',
                    ""GuideSummary""             TEXT    NOT NULL DEFAULT '',
                    ""ValidationSummary""        TEXT    NOT NULL DEFAULT '',
                    ""RawItemCount""             INTEGER NOT NULL DEFAULT 0,
                    ""AcceptedCount""            INTEGER NOT NULL DEFAULT 0,
                    ""SuppressedCount""          INTEGER NOT NULL DEFAULT 0,
                    ""DemotedCount""             INTEGER NOT NULL DEFAULT 0,
                    ""MatchedCount""             INTEGER NOT NULL DEFAULT 0,
                    ""UnmatchedCount""           INTEGER NOT NULL DEFAULT 0,
                    ""LiveCount""                INTEGER NOT NULL DEFAULT 0,
                    ""MovieCount""               INTEGER NOT NULL DEFAULT 0,
                    ""SeriesCount""              INTEGER NOT NULL DEFAULT 0,
                    ""EpisodeCount""             INTEGER NOT NULL DEFAULT 0,
                    ""AliasMatchCount""          INTEGER NOT NULL DEFAULT 0,
                    ""RegexMatchCount""          INTEGER NOT NULL DEFAULT 0,
                    ""FuzzyMatchCount""          INTEGER NOT NULL DEFAULT 0,
                    ""ProbeSuccessCount""        INTEGER NOT NULL DEFAULT 0,
                    ""ProbeFailureCount""        INTEGER NOT NULL DEFAULT 0,
                    ""WarningCount""             INTEGER NOT NULL DEFAULT 0,
                    ""ErrorCount""               INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_SourceAcquisitionRuns_SourceProfiles_SourceProfileId""
                        FOREIGN KEY (""SourceProfileId"")
                        REFERENCES ""SourceProfiles"" (""Id"")
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ""SourceAcquisitionEvidence"" (
                    ""Id""                     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceAcquisitionRunId"" INTEGER NOT NULL,
                    ""SourceProfileId""        INTEGER NOT NULL,
                    ""Stage""                  INTEGER NOT NULL DEFAULT 0,
                    ""Outcome""                INTEGER NOT NULL DEFAULT 0,
                    ""ItemKind""               INTEGER NOT NULL DEFAULT 0,
                    ""RuleCode""               TEXT    NOT NULL DEFAULT '',
                    ""Reason""                 TEXT    NOT NULL DEFAULT '',
                    ""RawName""                TEXT    NOT NULL DEFAULT '',
                    ""RawCategory""            TEXT    NOT NULL DEFAULT '',
                    ""NormalizedName""         TEXT    NOT NULL DEFAULT '',
                    ""NormalizedCategory""     TEXT    NOT NULL DEFAULT '',
                    ""IdentityKey""            TEXT    NOT NULL DEFAULT '',
                    ""AliasKeys""              TEXT    NOT NULL DEFAULT '',
                    ""MatchedValue""           TEXT    NOT NULL DEFAULT '',
                    ""MatchedTarget""          TEXT    NOT NULL DEFAULT '',
                    ""Confidence""             INTEGER NOT NULL DEFAULT 0,
                    ""SortOrder""              INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_SourceAcquisitionEvidence_SourceAcquisitionRuns_SourceAcquisitionRunId""
                        FOREIGN KEY (""SourceAcquisitionRunId"")
                        REFERENCES ""SourceAcquisitionRuns"" (""Id"")
                        ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureEpgSyncLogsTable(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""EpgSyncLogs"" (
                    ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceProfileId""     INTEGER NOT NULL,
                    ""SyncedAtUtc""         TEXT    NOT NULL,
                    ""LastSuccessAtUtc""    TEXT,
                    ""IsSuccess""           INTEGER NOT NULL DEFAULT 0,
                    ""Status""              INTEGER NOT NULL DEFAULT 0,
                    ""ResultCode""          INTEGER NOT NULL DEFAULT 0,
                    ""FailureStage""        INTEGER NOT NULL DEFAULT 0,
                    ""ActiveMode""          INTEGER NOT NULL DEFAULT 0,
                    ""ActiveXmltvUrl""      TEXT    NOT NULL DEFAULT '',
                    ""MatchedChannelCount"" INTEGER NOT NULL DEFAULT 0,
                    ""UnmatchedChannelCount"" INTEGER NOT NULL DEFAULT 0,
                    ""CurrentCoverageCount"" INTEGER NOT NULL DEFAULT 0,
                    ""NextCoverageCount""    INTEGER NOT NULL DEFAULT 0,
                    ""TotalLiveChannelCount"" INTEGER NOT NULL DEFAULT 0,
                    ""ProgrammeCount""      INTEGER NOT NULL DEFAULT 0,
                    ""MatchBreakdown""      TEXT    NOT NULL DEFAULT '',
                    ""FailureReason""       TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT ""FK_EpgSyncLogs_SourceProfiles_SourceProfileId""
                        FOREIGN KEY (""SourceProfileId"")
                        REFERENCES ""SourceProfiles"" (""Id"")
                        ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();

            // Unique index (safe to run even if already exists via CREATE TABLE)
            cmd.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_EpgSyncLogs_SourceProfileId""
                ON ""EpgSyncLogs"" (""SourceProfileId"");";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureSourceHealthTables(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""SourceHealthReports"" (
                    ""Id""                            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceProfileId""               INTEGER NOT NULL,
                    ""EvaluatedAtUtc""                TEXT    NOT NULL DEFAULT '',
                    ""LastSyncAttemptAtUtc""          TEXT,
                    ""LastSuccessfulSyncAtUtc""       TEXT,
                    ""HealthScore""                   INTEGER NOT NULL DEFAULT 0,
                    ""HealthState""                   INTEGER NOT NULL DEFAULT 0,
                    ""StatusSummary""                 TEXT    NOT NULL DEFAULT '',
                    ""ImportResultSummary""           TEXT    NOT NULL DEFAULT '',
                    ""ValidationSummary""             TEXT    NOT NULL DEFAULT '',
                    ""TopIssueSummary""               TEXT    NOT NULL DEFAULT '',
                    ""TotalChannelCount""             INTEGER NOT NULL DEFAULT 0,
                    ""TotalMovieCount""               INTEGER NOT NULL DEFAULT 0,
                    ""TotalSeriesCount""              INTEGER NOT NULL DEFAULT 0,
                    ""DuplicateCount""                INTEGER NOT NULL DEFAULT 0,
                    ""InvalidStreamCount""            INTEGER NOT NULL DEFAULT 0,
                    ""ChannelsWithEpgMatchCount""     INTEGER NOT NULL DEFAULT 0,
                    ""ChannelsWithCurrentProgramCount"" INTEGER NOT NULL DEFAULT 0,
                    ""ChannelsWithNextProgramCount""  INTEGER NOT NULL DEFAULT 0,
                    ""ChannelsWithLogoCount""         INTEGER NOT NULL DEFAULT 0,
                    ""SuspiciousEntryCount""          INTEGER NOT NULL DEFAULT 0,
                    ""WarningCount""                  INTEGER NOT NULL DEFAULT 0,
                    ""ErrorCount""                    INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_SourceHealthReports_SourceProfiles_SourceProfileId""
                        FOREIGN KEY (""SourceProfileId"")
                        REFERENCES ""SourceProfiles"" (""Id"")
                        ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SourceHealthReports_SourceProfileId""
                ON ""SourceHealthReports"" (""SourceProfileId"");";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""SourceHealthIssues"" (
                    ""Id""                     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceHealthReportId""   INTEGER NOT NULL,
                    ""Severity""               INTEGER NOT NULL DEFAULT 0,
                    ""Code""                   TEXT    NOT NULL DEFAULT '',
                    ""Title""                  TEXT    NOT NULL DEFAULT '',
                    ""Message""                TEXT    NOT NULL DEFAULT '',
                    ""AffectedCount""          INTEGER NOT NULL DEFAULT 0,
                    ""SampleItems""            TEXT    NOT NULL DEFAULT '',
                    ""SortOrder""              INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_SourceHealthIssues_SourceHealthReports_SourceHealthReportId""
                        FOREIGN KEY (""SourceHealthReportId"")
                        REFERENCES ""SourceHealthReports"" (""Id"")
                        ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS ""IX_SourceHealthIssues_SourceHealthReportId""
                ON ""SourceHealthIssues"" (""SourceHealthReportId"");";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""SourceHealthComponents"" (
                    ""Id""                     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceHealthReportId""   INTEGER NOT NULL,
                    ""ComponentType""          INTEGER NOT NULL DEFAULT 0,
                    ""State""                  INTEGER NOT NULL DEFAULT 0,
                    ""Score""                  INTEGER NOT NULL DEFAULT 0,
                    ""Summary""                TEXT    NOT NULL DEFAULT '',
                    ""RelevantCount""          INTEGER NOT NULL DEFAULT 0,
                    ""HealthyCount""           INTEGER NOT NULL DEFAULT 0,
                    ""IssueCount""             INTEGER NOT NULL DEFAULT 0,
                    ""SortOrder""              INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_SourceHealthComponents_SourceHealthReports_SourceHealthReportId""
                        FOREIGN KEY (""SourceHealthReportId"")
                        REFERENCES ""SourceHealthReports"" (""Id"")
                        ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS ""IX_SourceHealthComponents_SourceHealthReportId""
                ON ""SourceHealthComponents"" (""SourceHealthReportId"");";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SourceHealthComponents_SourceHealthReportId_ComponentType""
                ON ""SourceHealthComponents"" (""SourceHealthReportId"", ""ComponentType"");";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""SourceHealthProbes"" (
                    ""Id""                     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceHealthReportId""   INTEGER NOT NULL,
                    ""ProbeType""              INTEGER NOT NULL DEFAULT 0,
                    ""Status""                 INTEGER NOT NULL DEFAULT 0,
                    ""ProbedAtUtc""            TEXT,
                    ""CandidateCount""         INTEGER NOT NULL DEFAULT 0,
                    ""SampleSize""             INTEGER NOT NULL DEFAULT 0,
                    ""SuccessCount""           INTEGER NOT NULL DEFAULT 0,
                    ""FailureCount""           INTEGER NOT NULL DEFAULT 0,
                    ""TimeoutCount""           INTEGER NOT NULL DEFAULT 0,
                    ""HttpErrorCount""         INTEGER NOT NULL DEFAULT 0,
                    ""TransportErrorCount""    INTEGER NOT NULL DEFAULT 0,
                    ""Summary""                TEXT    NOT NULL DEFAULT '',
                    ""SortOrder""              INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_SourceHealthProbes_SourceHealthReports_SourceHealthReportId""
                        FOREIGN KEY (""SourceHealthReportId"")
                        REFERENCES ""SourceHealthReports"" (""Id"")
                        ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS ""IX_SourceHealthProbes_SourceHealthReportId""
                ON ""SourceHealthProbes"" (""SourceHealthReportId"");";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SourceHealthProbes_SourceHealthReportId_ProbeType""
                ON ""SourceHealthProbes"" (""SourceHealthReportId"", ""ProbeType"");";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureSourceEnrichmentTables(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""SourceChannelEnrichmentRecords"" (
                    ""Id""                        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceProfileId""           INTEGER NOT NULL,
                    ""IdentityKey""               TEXT    NOT NULL DEFAULT '',
                    ""NormalizedName""            TEXT    NOT NULL DEFAULT '',
                    ""AliasKeys""                 TEXT    NOT NULL DEFAULT '',
                    ""ProviderName""              TEXT    NOT NULL DEFAULT '',
                    ""ProviderEpgChannelId""      TEXT    NOT NULL DEFAULT '',
                    ""ProviderLogoUrl""           TEXT    NOT NULL DEFAULT '',
                    ""ResolvedLogoUrl""           TEXT    NOT NULL DEFAULT '',
                    ""MatchedXmltvChannelId""     TEXT    NOT NULL DEFAULT '',
                    ""MatchedXmltvDisplayName""   TEXT    NOT NULL DEFAULT '',
                    ""MatchedXmltvIconUrl""       TEXT    NOT NULL DEFAULT '',
                    ""EpgMatchSource""            INTEGER NOT NULL DEFAULT 0,
                    ""EpgMatchConfidence""        INTEGER NOT NULL DEFAULT 0,
                    ""EpgMatchSummary""           TEXT    NOT NULL DEFAULT '',
                    ""LogoSource""                INTEGER NOT NULL DEFAULT 0,
                    ""LogoConfidence""            INTEGER NOT NULL DEFAULT 0,
                    ""LogoSummary""               TEXT    NOT NULL DEFAULT '',
                    ""LastAppliedAtUtc""          TEXT    NOT NULL DEFAULT '',
                    ""LastSeenAtUtc""             TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT ""FK_SourceChannelEnrichmentRecords_SourceProfiles_SourceProfileId""
                        FOREIGN KEY (""SourceProfileId"")
                        REFERENCES ""SourceProfiles"" (""Id"")
                        ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS ""IX_SourceChannelEnrichmentRecords_SourceProfileId""
                ON ""SourceChannelEnrichmentRecords"" (""SourceProfileId"");";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SourceChannelEnrichmentRecords_SourceProfileId_IdentityKey""
                ON ""SourceChannelEnrichmentRecords"" (""SourceProfileId"", ""IdentityKey"");";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureOperationalTables(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""LogicalOperationalStates"" (
                    ""Id""                         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""ContentType""                INTEGER NOT NULL DEFAULT 0,
                    ""LogicalContentKey""          TEXT    NOT NULL DEFAULT '',
                    ""CandidateCount""             INTEGER NOT NULL DEFAULT 0,
                    ""PreferredContentId""         INTEGER NOT NULL DEFAULT 0,
                    ""PreferredSourceProfileId""   INTEGER NOT NULL DEFAULT 0,
                    ""PreferredScore""             INTEGER NOT NULL DEFAULT 0,
                    ""SelectionSummary""           TEXT    NOT NULL DEFAULT '',
                    ""LastKnownGoodContentId""     INTEGER NOT NULL DEFAULT 0,
                    ""LastKnownGoodSourceProfileId"" INTEGER NOT NULL DEFAULT 0,
                    ""LastKnownGoodScore""         INTEGER NOT NULL DEFAULT 0,
                    ""LastKnownGoodAtUtc""         TEXT,
                    ""LastPlaybackSuccessAtUtc""   TEXT,
                    ""LastPlaybackFailureAtUtc""   TEXT,
                    ""ConsecutivePlaybackFailures"" INTEGER NOT NULL DEFAULT 0,
                    ""RecoveryAction""             INTEGER NOT NULL DEFAULT 0,
                    ""RecoverySummary""            TEXT    NOT NULL DEFAULT '',
                    ""SnapshotEvaluatedAtUtc""     TEXT    NOT NULL DEFAULT '',
                    ""PreferredUpdatedAtUtc""      TEXT
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""LogicalOperationalCandidates"" (
                    ""Id""                       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""LogicalOperationalStateId"" INTEGER NOT NULL,
                    ""ContentId""               INTEGER NOT NULL DEFAULT 0,
                    ""SourceProfileId""         INTEGER NOT NULL DEFAULT 0,
                    ""Rank""                    INTEGER NOT NULL DEFAULT 0,
                    ""Score""                   INTEGER NOT NULL DEFAULT 0,
                    ""IsSelected""              INTEGER NOT NULL DEFAULT 0,
                    ""IsLastKnownGood""         INTEGER NOT NULL DEFAULT 0,
                    ""SupportsProxy""           INTEGER NOT NULL DEFAULT 0,
                    ""SourceName""              TEXT    NOT NULL DEFAULT '',
                    ""StreamUrl""               TEXT    NOT NULL DEFAULT '',
                    ""Summary""                 TEXT    NOT NULL DEFAULT '',
                    ""LastSeenAtUtc""           TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT ""FK_LogicalOperationalCandidates_LogicalOperationalStates_LogicalOperationalStateId""
                        FOREIGN KEY (""LogicalOperationalStateId"")
                        REFERENCES ""LogicalOperationalStates"" (""Id"")
                        ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureColumn(SqliteConnection conn, string tableName, string columnName, string definition)
        {
            if (!TableExists(conn, tableName) || ColumnExists(conn, tableName, columnName))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {definition};";
            cmd.ExecuteNonQuery();
        }

        private static bool TableExists(SqliteConnection conn, string tableName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$tableName LIMIT 1;";
            cmd.Parameters.AddWithValue("$tableName", tableName);
            return cmd.ExecuteScalar() != null;
        }

        private static bool ColumnExists(SqliteConnection conn, string tableName, string columnName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureIndex(SqliteConnection conn, string indexName, string tableName, string columnName, bool unique = false)
        {
            if (!TableExists(conn, tableName) || !ColumnExists(conn, tableName, columnName) || IndexExists(conn, indexName))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE {(unique ? "UNIQUE " : string.Empty)}INDEX \"{indexName}\" ON \"{tableName}\" (\"{columnName}\");";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureCompositeIndex(SqliteConnection conn, string indexName, string tableName, string col1, string col2)
        {
            if (!TableExists(conn, tableName) || IndexExists(conn, indexName))
            {
                return;
            }

            if (!ColumnExists(conn, tableName, col1) || !ColumnExists(conn, tableName, col2))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE INDEX \"{indexName}\" ON \"{tableName}\" (\"{col1}\", \"{col2}\");";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureUniqueCompositeIndex(SqliteConnection conn, string indexName, string tableName, string col1, string col2)
        {
            if (!TableExists(conn, tableName) || IndexExists(conn, indexName))
            {
                return;
            }

            if (!ColumnExists(conn, tableName, col1) || !ColumnExists(conn, tableName, col2))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE UNIQUE INDEX \"{indexName}\" ON \"{tableName}\" (\"{col1}\", \"{col2}\");";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureTripleCompositeIndex(SqliteConnection conn, string indexName, string tableName, string col1, string col2, string col3)
        {
            if (!TableExists(conn, tableName) || IndexExists(conn, indexName))
            {
                return;
            }

            if (!ColumnExists(conn, tableName, col1) || !ColumnExists(conn, tableName, col2) || !ColumnExists(conn, tableName, col3))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE INDEX \"{indexName}\" ON \"{tableName}\" (\"{col1}\", \"{col2}\", \"{col3}\");";
            cmd.ExecuteNonQuery();
        }

        private static bool IndexExists(SqliteConnection conn, string indexName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='index' AND name=$indexName LIMIT 1;";
            cmd.Parameters.AddWithValue("$indexName", indexName);
            return cmd.ExecuteScalar() != null;
        }

        private static bool IsRecoverableMigrationDrift(Exception ex)
        {
            var message = ex.ToString();
            return message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("duplicate index name", StringComparison.OrdinalIgnoreCase);
        }
    }
}

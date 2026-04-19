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
                string backupPath = dbPath + ".bak";
                File.Copy(dbPath, backupPath, overwrite: true);

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
                EnsureRuntimeSchema(dbPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to migrate database securely. App initialization halted. Error: {ex.Message}");
            }
        }

        private static void EnsureRuntimeSchema(string dbPath)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            EnsureAppProfilesTable(conn);
            EnsureDefaultProfile(conn);
            EnsureActiveProfileSetting(conn);

            EnsureColumn(conn, "Favorites", "ProfileId", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "PlaybackProgresses", "ProfileId", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(conn, "PlaybackProgresses", "DurationMs", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "PlaybackProgresses", "WatchStateOverride", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "PlaybackProgresses", "CompletedAtUtc", "TEXT");
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
            EnsureColumn(conn, "EpgPrograms", "Subtitle", "TEXT");
            EnsureColumn(conn, "EpgPrograms", "Category", "TEXT");

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

            // EPG pass: per-source sync health log (CREATE TABLE IF NOT EXISTS is safe to repeat)
            EnsureEpgSyncLogsTable(conn);

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
            EnsureIndex(conn, "IX_ParentalControlSettings_ProfileId", "ParentalControlSettings", "ProfileId", unique: true);
            EnsureTripleCompositeIndex(conn, "IX_Favorites_ProfileId_ContentType_ContentId", "Favorites", "ProfileId", "ContentType", "ContentId");
            EnsureTripleCompositeIndex(conn, "IX_PlaybackProgresses_ProfileId_ContentType_ContentId", "PlaybackProgresses", "ProfileId", "ContentType", "ContentId");
            EnsureTripleCompositeIndex(conn, "IX_RecordingJobs_ProfileId_Status_StartTimeUtc", "RecordingJobs", "ProfileId", "Status", "StartTimeUtc");
            EnsureTripleCompositeIndex(conn, "IX_DownloadJobs_ProfileId_Status_RequestedAtUtc", "DownloadJobs", "ProfileId", "Status", "RequestedAtUtc");
            EnsureCompositeIndex(conn, "IX_EpgPrograms_ChannelId_StartTimeUtc", "EpgPrograms", "ChannelId", "StartTimeUtc");

            BackfillLegacyProfileState(conn);
        }

        private static void BumpLegacyM3uImportMode(SqliteConnection conn)
        {
            if (!TableExists(conn, "SourceCredentials")) return;
            if (!ColumnExists(conn, "SourceCredentials", "M3uImportMode")) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE \"SourceCredentials\" SET \"M3uImportMode\" = 2 WHERE \"M3uImportMode\" = 1;";
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

        private static void EnsureEpgSyncLogsTable(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""EpgSyncLogs"" (
                    ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceProfileId""     INTEGER NOT NULL,
                    ""SyncedAtUtc""         TEXT    NOT NULL,
                    ""IsSuccess""           INTEGER NOT NULL DEFAULT 0,
                    ""MatchedChannelCount"" INTEGER NOT NULL DEFAULT 0,
                    ""ProgrammeCount""      INTEGER NOT NULL DEFAULT 0,
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
    }
}

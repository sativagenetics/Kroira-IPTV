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

            EnsureColumn(conn, "Movies", "BackdropUrl", "TEXT");
            EnsureColumn(conn, "Movies", "Genres", "TEXT");
            EnsureColumn(conn, "Movies", "ImdbId", "TEXT");
            EnsureColumn(conn, "Movies", "MetadataUpdatedAt", "TEXT");
            EnsureColumn(conn, "Movies", "OriginalLanguage", "TEXT");
            EnsureColumn(conn, "Movies", "Overview", "TEXT");
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
            EnsureColumn(conn, "Series", "Popularity", "REAL NOT NULL DEFAULT 0.0");
            EnsureColumn(conn, "Series", "TmdbBackdropPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "TmdbPosterPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "Series", "VoteAverage", "REAL NOT NULL DEFAULT 0.0");

            EnsureIndex(conn, "IX_Movies_MetadataUpdatedAt", "Movies", "MetadataUpdatedAt");
            EnsureIndex(conn, "IX_Movies_TmdbId", "Movies", "TmdbId");
            EnsureIndex(conn, "IX_Series_MetadataUpdatedAt", "Series", "MetadataUpdatedAt");
            EnsureIndex(conn, "IX_Series_TmdbId", "Series", "TmdbId");
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

        private static void EnsureIndex(SqliteConnection conn, string indexName, string tableName, string columnName)
        {
            if (!TableExists(conn, tableName) || !ColumnExists(conn, tableName, columnName) || IndexExists(conn, indexName))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE INDEX \"{indexName}\" ON \"{tableName}\" (\"{columnName}\");";
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

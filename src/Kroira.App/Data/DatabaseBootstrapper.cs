using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Data
{
    public static class DatabaseBootstrapper
    {
        public static void Initialize(AppDbContext context)
        {
            var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira");
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }

            string dbPath = Path.Combine(dbFolder, "kroira.db");

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
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to migrate database securely. App initialization halted. Error: {ex.Message}");
            }
        }
    }
}

#nullable enable
using System;
using System.Diagnostics;
using System.IO;

namespace Kroira.App.Services
{
    internal static class CatalogCountDiagnosticsLogger
    {
        private static readonly object Sync = new();

        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "startup-log.txt");

        internal static void Log(
            string context,
            string mediaType,
            string sourceProfileId,
            int? parserCount,
            int persistedCount,
            int queriedCount,
            int surfacedCount,
            string? notes = null)
        {
            try
            {
                var line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] CATALOG COUNTS context={context}; source_profile_id={sourceProfileId}; media_type={mediaType}; parser_count={Format(parserCount)}; persisted_count={persistedCount}; queried_count={queriedCount}; surfaced_count={surfacedCount}; notes={Format(notes)}{Environment.NewLine}";
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                lock (Sync)
                {
                    Debug.WriteLine(line);
                    File.AppendAllText(LogPath, line);
                }
            }
            catch
            {
            }
        }

        private static string Format(int? value)
        {
            return value.HasValue ? value.Value.ToString() : "\"\"";
        }

        private static string Format(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return $"\"{value.Replace("\"", "'")}\"";
        }
    }
}

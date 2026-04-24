using System;
using System.IO;

namespace Kroira.App.Services
{
    internal static class RuntimeEventLogger
    {
        private static readonly object Sync = new();
        private static readonly ISensitiveDataRedactionService Redactor = new SensitiveDataRedactionService();

        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "startup-log.txt");

        internal static void Log(string area, string message)
        {
            try
            {
                var safeMessage = Redactor.RedactLooseText(message);
                var line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {area} {safeMessage}; thread={Environment.CurrentManagedThreadId}{Environment.NewLine}";
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                lock (Sync)
                {
                    File.AppendAllText(LogPath, line);
                }
            }
            catch
            {
            }
        }

        internal static void Log(string area, Exception ex, string message)
        {
            Log(area, $"{message} - {ex.GetType().Name}: {ex.Message}");
        }
    }
}

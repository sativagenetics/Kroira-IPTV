using System;
using System.IO;
using System.Threading;

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

        private static string DiagnosticsLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "diagnostics-log.txt");

        internal static void Log(string area, string message)
        {
            var safeMessage = Redactor.RedactLooseText(message);
            var line =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {area} {safeMessage}; thread={Environment.CurrentManagedThreadId}{Environment.NewLine}";
            QueueWrite(LogPath, line);
        }

        internal static void LogEvent(string eventName, string details = "")
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                return;
            }

            var safeEvent = eventName.Trim()
                .Replace(" ", "_", StringComparison.Ordinal)
                .ToLowerInvariant();
            var safeDetails = Redactor.RedactLooseText(details);
            var line =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] event={safeEvent}; {safeDetails}; thread={Environment.CurrentManagedThreadId}{Environment.NewLine}";
            QueueWrite(DiagnosticsLogPath, line);
        }

        internal static void LogEvent(string eventName, Exception ex, string details = "")
        {
            LogEvent(eventName, $"{details}; exception={ex.GetType().Name}; message={ex.Message}");
        }

        private static void QueueWrite(string path, string line)
        {
            try
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        lock (Sync)
                        {
                            File.AppendAllText(path, line);
                        }
                    }
                    catch
                    {
                    }
                });
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

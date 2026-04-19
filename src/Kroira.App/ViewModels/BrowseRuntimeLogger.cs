#nullable enable
using System;
using System.IO;

namespace Kroira.App.ViewModels
{
    internal static class BrowseRuntimeLogger
    {
        private static readonly object Sync = new();

        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "startup-log.txt");

        internal static void Log(string area, string message)
        {
            try
            {
                var line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {area} {message}; thread={Environment.CurrentManagedThreadId}{Environment.NewLine}";
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
    }
}

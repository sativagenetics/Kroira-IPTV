using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kroira.App.Data
{
    public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        private const string DbPathArgumentPrefix = "--db-path=";

        public AppDbContext CreateDbContext(string[] args)
        {
            var dbPath = ResolveDatabasePath(args);
            var dbDirectory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            return new AppDbContext(options);
        }

        private static string ResolveDatabasePath(string[] args)
        {
            foreach (var arg in args)
            {
                if (!arg.StartsWith(DbPathArgumentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var argumentPath = arg[DbPathArgumentPrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(argumentPath))
                {
                    return Path.GetFullPath(argumentPath);
                }
            }

            var environmentPath = Environment.GetEnvironmentVariable("KROIRA_EF_DB_PATH");
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                return Path.GetFullPath(environmentPath);
            }

            return Path.Combine(Path.GetTempPath(), "Kroira", "ef-design", "kroira.design.db");
        }
    }
}

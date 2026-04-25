using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class SettingsSerializationTests
{
    [TestMethod]
    public async Task SourceAutoRefreshSettings_SaveLoadRoundtripClampsInterval()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var service = new SourceAutoRefreshService(
            new ServiceCollection().BuildServiceProvider(),
            new NoopSourceRefreshService());

        await service.SaveSettingsAsync(
            db,
            new SourceAutoRefreshSettings
            {
                IsEnabled = false,
                IntervalHours = 99,
                RunAfterLaunch = false
            });
        db.ChangeTracker.Clear();

        var loaded = await service.LoadSettingsAsync(db);

        Assert.IsFalse(loaded.IsEnabled);
        Assert.AreEqual(24, loaded.IntervalHours);
        Assert.IsFalse(loaded.RunAfterLaunch);
    }

    [TestMethod]
    public async Task SourceAutoRefreshSettings_InvalidSerializedValuesFallBackSafely()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        db.AppSettings.AddRange(
            new AppSetting { Key = "Sources.AutoRefresh.Enabled", Value = "not-a-bool" },
            new AppSetting { Key = "Sources.AutoRefresh.IntervalHours", Value = "0" },
            new AppSetting { Key = "Sources.AutoRefresh.RunAfterLaunch", Value = "not-a-bool" });
        await db.SaveChangesAsync();

        var service = new SourceAutoRefreshService(
            new ServiceCollection().BuildServiceProvider(),
            new NoopSourceRefreshService());
        var loaded = await service.LoadSettingsAsync(db);

        Assert.IsTrue(loaded.IsEnabled);
        Assert.AreEqual(1, loaded.IntervalHours);
        Assert.IsTrue(loaded.RunAfterLaunch);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AppDbContext> CreateDatabaseAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class NoopSourceRefreshService : ISourceRefreshService
    {
        public Task<SourceRefreshResult> RefreshSourceAsync(
            int sourceProfileId,
            SourceRefreshTrigger trigger,
            SourceRefreshScope scope)
        {
            return Task.FromResult(new SourceRefreshResult
            {
                SourceProfileId = sourceProfileId,
                Trigger = trigger,
                Scope = scope,
                Success = false,
                Message = "Not used by serialization tests."
            });
        }
    }
}

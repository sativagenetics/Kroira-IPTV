using System.Data;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class DataIntegrityMigrationTests
{
    [TestMethod]
    public async Task FreshDatabase_MigrateOrRepairCreatesCleanNoSourceSchema()
    {
        var dbPath = CreateTempDatabasePath();
        try
        {
            await using var db = CreateFileDatabase(dbPath);

            await MigrateOrRepairAsync(db);
            var profile = await new ProfileStateService().GetActiveProfileAsync(db);

            Assert.IsNotNull(profile);
            Assert.AreEqual(0, await db.SourceProfiles.CountAsync());

            var tables = await LoadSqliteObjectNamesAsync(db, "table");
            var indexes = await LoadSqliteObjectNamesAsync(db, "index");
            AssertContains(tables, "SourceProtectedCredentialSecrets");
            AssertContains(indexes, "IX_SourceProtectedCredentialSecrets_SourceProfileId_Name");
            AssertContains(indexes, "IX_EpgPrograms_ChannelId_StartTimeUtc_EndTimeUtc");
            AssertContains(indexes, "IX_Channels_ChannelCategoryId_ProviderEpgChannelId");
            AssertContains(indexes, "IX_Movies_SourceProfileId_CanonicalTitleKey");
            AssertContains(indexes, "IX_Series_SourceProfileId_CanonicalTitleKey");
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [TestMethod]
    public async Task BootstrapRepair_IsIdempotentAfterHistoricalMigrationDrift()
    {
        var dbPath = CreateTempDatabasePath();
        try
        {
            await using (var firstRun = CreateFileDatabase(dbPath))
            {
                await MigrateOrRepairAsync(firstRun);
            }

            await using var secondRun = CreateFileDatabase(dbPath);
            await MigrateOrRepairAsync(secondRun);

            var tables = await LoadSqliteObjectNamesAsync(secondRun, "table");
            var indexes = await LoadSqliteObjectNamesAsync(secondRun, "index");
            AssertContains(tables, "SourceProtectedCredentialSecrets");
            AssertContains(indexes, "IX_SourceProtectedCredentialSecrets_SourceProfileId_Name");
            AssertContains(indexes, "IX_EpgMappingDecisions_SourceProfileId_ChannelIdentityKey");
            AssertContains(indexes, "IX_EpgPrograms_StartTimeUtc_EndTimeUtc");
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [TestMethod]
    public async Task SourceDeletionCleanup_RemovesOwnedRowsAndRepointsPersonalStateToRemainingCopy()
    {
        await using var connection = await OpenConnectionAsync();
        using var provider = CreateLifecycleProvider(connection);
        int sourceToDeleteId;
        int remainingSourceId;
        int deletedMovieId;
        int remainingMovieId;
        int deletedChannelId;
        var logicalKey = "movie:test:shared-title";

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var profile = await new ProfileStateService().GetActiveProfileAsync(db);
            var seed = await SeedSourceDeleteGraphAsync(db, profile.Id, logicalKey);
            sourceToDeleteId = seed.SourceToDeleteId;
            remainingSourceId = seed.RemainingSourceId;
            deletedMovieId = seed.DeletedMovieId;
            remainingMovieId = seed.RemainingMovieId;
            deletedChannelId = seed.DeletedChannelId;
        }

        var result = await provider.GetRequiredService<ISourceLifecycleService>()
            .DeleteSourceAsync(sourceToDeleteId);

        Assert.IsTrue(result.Success, result.Message);

        using var verifyScope = provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.IsFalse(await verifyDb.SourceProfiles.AnyAsync(item => item.Id == sourceToDeleteId));
        Assert.IsTrue(await verifyDb.SourceProfiles.AnyAsync(item => item.Id == remainingSourceId));
        Assert.IsFalse(await verifyDb.Movies.AnyAsync(item => item.Id == deletedMovieId));
        Assert.IsFalse(await verifyDb.Channels.AnyAsync(item => item.Id == deletedChannelId));
        Assert.AreEqual(0, await verifyDb.EpgPrograms.CountAsync());
        Assert.AreEqual(0, await verifyDb.EpgMappingDecisions.CountAsync(item => item.SourceProfileId == sourceToDeleteId || item.ChannelId == deletedChannelId));
        Assert.AreEqual(0, await verifyDb.SourceCredentials.CountAsync(item => item.SourceProfileId == sourceToDeleteId));
        Assert.AreEqual(0, await verifyDb.SourceProtectedCredentialSecrets.CountAsync(item => item.SourceProfileId == sourceToDeleteId));
        Assert.AreEqual(0, await verifyDb.SourceSyncStates.CountAsync(item => item.SourceProfileId == sourceToDeleteId));
        Assert.AreEqual(0, await verifyDb.EpgSyncLogs.CountAsync(item => item.SourceProfileId == sourceToDeleteId));
        Assert.AreEqual(0, await verifyDb.SourceHealthReports.CountAsync(item => item.SourceProfileId == sourceToDeleteId));

        var favorite = await verifyDb.Favorites.SingleAsync();
        Assert.AreEqual(remainingMovieId, favorite.ContentId);
        Assert.AreEqual(remainingSourceId, favorite.PreferredSourceProfileId);
        Assert.AreEqual(logicalKey, favorite.LogicalContentKey);

        var progress = await verifyDb.PlaybackProgresses.SingleAsync();
        Assert.AreEqual(remainingMovieId, progress.ContentId);
        Assert.AreEqual(remainingSourceId, progress.PreferredSourceProfileId);
        Assert.AreEqual(logicalKey, progress.LogicalContentKey);
    }

    [TestMethod]
    public async Task CurrentNextGuideQuery_WorksAfterMigrationRepair()
    {
        var dbPath = CreateTempDatabasePath();
        try
        {
            await using var db = CreateFileDatabase(dbPath);
            await MigrateOrRepairAsync(db);
            var nowUtc = new DateTime(2030, 1, 2, 12, 15, 0, DateTimeKind.Utc);
            var channelId = await SeedGuideChannelAsync(db, "Guide Source", "News", "World News", "world.news");
            await SeedProgramAsync(db, channelId, "Current Bulletin", nowUtc.AddMinutes(-15), nowUtc.AddMinutes(15));
            await SeedProgramAsync(db, channelId, "Next Bulletin", nowUtc.AddMinutes(15), nowUtc.AddMinutes(45));

            var result = await new EpgGuideTimelineService().BuildTimelineAsync(
                db,
                new EpgGuideTimelineRequest
                {
                    RangeStartUtc = nowUtc.AddMinutes(-15),
                    RangeDuration = TimeSpan.FromHours(2),
                    SlotDuration = TimeSpan.FromMinutes(30),
                    NowUtc = nowUtc
                });

            Assert.AreEqual(1, result.Channels.Count);
            Assert.AreEqual("Current Bulletin", result.Channels[0].CurrentProgram?.Title);
            Assert.AreEqual("Next Bulletin", result.Channels[0].NextProgram?.Title);
            Assert.IsTrue(result.HasGuideData);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    [TestMethod]
    public async Task ManualOverridePersistence_ReloadsApprovedDecisionAfterMigrationRepair()
    {
        var dbPath = CreateTempDatabasePath();
        try
        {
            int channelId;
            await using (var db = CreateFileDatabase(dbPath))
            {
                await MigrateOrRepairAsync(db);
                channelId = await SeedGuideChannelAsync(db, "Manual Match Source", "Sports", "Match Arena", "match.arena");
                db.EpgMappingDecisions.Add(new EpgMappingDecision
                {
                    SourceProfileId = await db.ChannelCategories
                        .Where(category => db.Channels.Any(channel => channel.Id == channelId && channel.ChannelCategoryId == category.Id))
                        .Select(category => category.SourceProfileId)
                        .SingleAsync(),
                    ChannelId = channelId,
                    ChannelIdentityKey = "channel:match-arena",
                    ChannelName = "Match Arena",
                    CategoryName = "Sports",
                    ProviderEpgChannelId = "provider.match",
                    XmltvChannelId = "xmltv.match",
                    XmltvDisplayName = "XMLTV Match Arena",
                    Decision = EpgMappingDecisionState.Approved,
                    SuggestedMatchSource = ChannelEpgMatchSource.UserApproved,
                    SuggestedConfidence = 100,
                    ReasonSummary = "User approved",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            await using var reloaded = CreateFileDatabase(dbPath);
            var decision = await reloaded.EpgMappingDecisions.AsNoTracking().SingleAsync();
            Assert.AreEqual(channelId, decision.ChannelId);
            Assert.AreEqual("xmltv.match", decision.XmltvChannelId);
            Assert.AreEqual(EpgMappingDecisionState.Approved, decision.Decision);
            Assert.AreEqual(ChannelEpgMatchSource.UserApproved, decision.SuggestedMatchSource);
        }
        finally
        {
            TryDeleteDatabase(dbPath);
        }
    }

    private static ServiceProvider CreateLifecycleProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IBrowsePreferencesService, BrowsePreferencesService>();
        services.AddSingleton<ILiveChannelIdentityService, LiveChannelIdentityService>();
        services.AddSingleton<ILogicalCatalogStateService, LogicalCatalogStateService>();
        services.AddSingleton<IContentOperationalService, NoopContentOperationalService>();
        services.AddSingleton<ISourceAutoRefreshService, NoopSourceAutoRefreshService>();
        services.AddSingleton<ISourceLifecycleService, SourceLifecycleService>();
        return services.BuildServiceProvider();
    }

    private static async Task<SourceDeleteSeedResult> SeedSourceDeleteGraphAsync(
        AppDbContext db,
        int profileId,
        string logicalKey)
    {
        var nowUtc = DateTime.UtcNow;
        var sourceToDelete = new SourceProfile
        {
            Name = "Delete Me",
            Type = SourceType.M3U,
            LastSync = nowUtc
        };
        var remainingSource = new SourceProfile
        {
            Name = "Keep Me",
            Type = SourceType.M3U,
            LastSync = nowUtc
        };
        db.SourceProfiles.AddRange(sourceToDelete, remainingSource);
        await db.SaveChangesAsync();

        var category = new ChannelCategory
        {
            SourceProfileId = sourceToDelete.Id,
            Name = "News",
            OrderIndex = 0
        };
        db.ChannelCategories.Add(category);
        await db.SaveChangesAsync();

        var identity = new LiveChannelIdentityService().Build("World News", "world.news");
        var channel = new Channel
        {
            ChannelCategoryId = category.Id,
            Name = "World News",
            StreamUrl = "https://stream.example/live/world-news.m3u8",
            EpgChannelId = "world.news",
            ProviderEpgChannelId = "world.news",
            NormalizedIdentityKey = identity.IdentityKey,
            NormalizedName = identity.NormalizedName,
            AliasKeys = string.Join("\n", identity.AliasKeys)
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        db.EpgPrograms.Add(new EpgProgram
        {
            ChannelId = channel.Id,
            Title = "Delete Me Programme",
            StartTimeUtc = nowUtc.AddMinutes(-30),
            EndTimeUtc = nowUtc.AddMinutes(30)
        });
        db.EpgMappingDecisions.Add(new EpgMappingDecision
        {
            SourceProfileId = sourceToDelete.Id,
            ChannelId = channel.Id,
            ChannelIdentityKey = identity.IdentityKey,
            ChannelName = channel.Name,
            CategoryName = category.Name,
            ProviderEpgChannelId = channel.ProviderEpgChannelId,
            XmltvChannelId = "world.news",
            XmltvDisplayName = "World News",
            Decision = EpgMappingDecisionState.Approved,
            SuggestedMatchSource = ChannelEpgMatchSource.UserApproved,
            SuggestedConfidence = 100,
            ReasonSummary = "User approved",
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        });

        var deletedMovie = new Movie
        {
            SourceProfileId = sourceToDelete.Id,
            Title = "Shared Title",
            StreamUrl = "https://stream.example/movie/delete.mp4",
            DedupFingerprint = logicalKey,
            CanonicalTitleKey = "shared-title",
            CategoryName = "Movies",
            RawSourceCategoryName = "Movies"
        };
        var remainingMovie = new Movie
        {
            SourceProfileId = remainingSource.Id,
            Title = "Shared Title",
            StreamUrl = "https://stream.example/movie/keep.mp4",
            DedupFingerprint = logicalKey,
            CanonicalTitleKey = "shared-title",
            CategoryName = "Movies",
            RawSourceCategoryName = "Movies"
        };
        db.Movies.AddRange(deletedMovie, remainingMovie);
        await db.SaveChangesAsync();

        db.SourceCredentials.Add(new SourceCredential
        {
            SourceProfileId = sourceToDelete.Id,
            Url = "https://iptv.example"
        });
        db.SourceProtectedCredentialSecrets.Add(new SourceProtectedCredentialSecret
        {
            SourceProfileId = sourceToDelete.Id,
            Name = "Password",
            ProtectedValue = "protected-value",
            ProtectionScheme = "fake-v1",
            UpdatedAtUtc = nowUtc
        });
        db.SourceSyncStates.Add(new SourceSyncState
        {
            SourceProfileId = sourceToDelete.Id,
            LastAttempt = nowUtc,
            AutoRefreshSummary = "stale"
        });
        db.EpgSyncLogs.Add(new EpgSyncLog
        {
            SourceProfileId = sourceToDelete.Id,
            SyncedAtUtc = nowUtc,
            LastSuccessAtUtc = nowUtc,
            IsSuccess = true,
            Status = EpgStatus.Ready
        });
        db.SourceHealthReports.Add(new SourceHealthReport
        {
            SourceProfileId = sourceToDelete.Id,
            EvaluatedAtUtc = nowUtc,
            LastSyncAttemptAtUtc = nowUtc,
            LastSuccessfulSyncAtUtc = nowUtc,
            HealthScore = 80,
            HealthState = SourceHealthState.Good,
            StatusSummary = "Good"
        });
        db.Favorites.Add(new Favorite
        {
            ProfileId = profileId,
            ContentType = FavoriteType.Movie,
            ContentId = deletedMovie.Id,
            LogicalContentKey = logicalKey,
            PreferredSourceProfileId = sourceToDelete.Id,
            ResolvedAtUtc = nowUtc
        });
        db.PlaybackProgresses.Add(new PlaybackProgress
        {
            ProfileId = profileId,
            ContentType = PlaybackContentType.Movie,
            ContentId = deletedMovie.Id,
            LogicalContentKey = logicalKey,
            PreferredSourceProfileId = sourceToDelete.Id,
            PositionMs = 120_000,
            DurationMs = 600_000,
            LastWatched = nowUtc,
            ResolvedAtUtc = nowUtc
        });
        await db.SaveChangesAsync();

        return new SourceDeleteSeedResult(
            sourceToDelete.Id,
            remainingSource.Id,
            deletedMovie.Id,
            remainingMovie.Id,
            channel.Id);
    }

    private static async Task<int> SeedGuideChannelAsync(
        AppDbContext db,
        string sourceName,
        string categoryName,
        string channelName,
        string providerEpgChannelId)
    {
        var source = new SourceProfile
        {
            Name = sourceName,
            Type = SourceType.M3U
        };
        db.SourceProfiles.Add(source);
        await db.SaveChangesAsync();

        var category = new ChannelCategory
        {
            SourceProfileId = source.Id,
            Name = categoryName,
            OrderIndex = 0
        };
        db.ChannelCategories.Add(category);
        await db.SaveChangesAsync();

        var identity = new LiveChannelIdentityService().Build(channelName, providerEpgChannelId);
        var channel = new Channel
        {
            ChannelCategoryId = category.Id,
            Name = channelName,
            StreamUrl = $"https://stream.example/live/{providerEpgChannelId}.m3u8",
            EpgChannelId = providerEpgChannelId,
            ProviderEpgChannelId = providerEpgChannelId,
            NormalizedIdentityKey = identity.IdentityKey,
            NormalizedName = identity.NormalizedName,
            AliasKeys = string.Join("\n", identity.AliasKeys),
            EpgMatchSource = ChannelEpgMatchSource.Provider,
            EpgMatchConfidence = 97
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

    private static async Task SeedProgramAsync(
        AppDbContext db,
        int channelId,
        string title,
        DateTime startUtc,
        DateTime endUtc)
    {
        db.EpgPrograms.Add(new EpgProgram
        {
            ChannelId = channelId,
            Title = title,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc
        });
        await db.SaveChangesAsync();
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static AppDbContext CreateFileDatabase(string dbPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task MigrateOrRepairAsync(AppDbContext db)
    {
        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex) when (IsRecoverableMigrationDrift(ex))
        {
            DatabaseBootstrapper.EnsureRuntimeSchema(db);
            return;
        }

        DatabaseBootstrapper.EnsureRuntimeSchema(db);
    }

    private static bool IsRecoverableMigrationDrift(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate index name", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HashSet<string>> LoadSqliteObjectNamesAsync(AppDbContext db, string objectType)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$type";
        parameter.Value = objectType;
        command.Parameters.Add(parameter);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static void AssertContains(HashSet<string> values, string expected)
    {
        Assert.IsTrue(values.Contains(expected), $"Expected SQLite object '{expected}' to exist.");
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"kroira-v2-data-{Guid.NewGuid():N}.db");
    }

    private static void TryDeleteDatabase(string dbPath)
    {
        foreach (var path in new[] { dbPath, $"{dbPath}-shm", $"{dbPath}-wal" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup for temp SQLite artifacts used by tests.
            }
        }
    }

    private sealed record SourceDeleteSeedResult(
        int SourceToDeleteId,
        int RemainingSourceId,
        int DeletedMovieId,
        int RemainingMovieId,
        int DeletedChannelId);

    private sealed class NoopContentOperationalService : IContentOperationalService
    {
        public Task RefreshOperationalStateAsync(AppDbContext db)
        {
            return Task.CompletedTask;
        }

        public Task<OperationalPlaybackResolution?> ResolvePlaybackContextAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            IReadOnlyCollection<int>? excludedContentIds = null)
        {
            return Task.FromResult<OperationalPlaybackResolution?>(null);
        }

        public Task MarkPlaybackSucceededAsync(AppDbContext db, PlaybackLaunchContext context)
        {
            return Task.CompletedTask;
        }

        public Task<OperationalPlaybackResolution?> MarkPlaybackFailedAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            string reason,
            IReadOnlyCollection<int>? excludedContentIds = null)
        {
            return Task.FromResult<OperationalPlaybackResolution?>(null);
        }
    }

    private sealed class NoopSourceAutoRefreshService : ISourceAutoRefreshService
    {
        public Task<SourceAutoRefreshSettings> LoadSettingsAsync(AppDbContext db)
        {
            return Task.FromResult(new SourceAutoRefreshSettings());
        }

        public Task SaveSettingsAsync(AppDbContext db, SourceAutoRefreshSettings settings)
        {
            return Task.CompletedTask;
        }

        public Task UpdateScheduleAsync(AppDbContext db, int sourceProfileId, SourceRefreshTrigger trigger, bool success, string summary)
        {
            return Task.CompletedTask;
        }

        public Task RepairRuntimeStateAsync(AppDbContext db)
        {
            return Task.CompletedTask;
        }

        public Task RefreshDueSourcesAsync(bool runOverdueOnly)
        {
            return Task.CompletedTask;
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }
    }
}

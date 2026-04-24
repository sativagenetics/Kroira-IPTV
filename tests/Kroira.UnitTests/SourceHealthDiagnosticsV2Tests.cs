using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class SourceHealthDiagnosticsV2Tests
{
    [TestMethod]
    public async Task HealthySource_ScoresHealthyWithProgrammeBackedCoverage()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var nowUtc = DateTime.UtcNow;
        var sourceId = await SeedSourceAsync(db, "Healthy Source", lastSyncUtc: nowUtc.AddMinutes(-10), detectedEpgUrl: "https://guide.example/xmltv.xml");
        var news = await SeedChannelAsync(db, sourceId, "News", "World News", "https://stream.example/news.m3u8", "news", logoUrl: "https://img.example/news.png");
        var sport = await SeedChannelAsync(db, sourceId, "Sports", "Match Arena", "https://stream.example/sport.m3u8", "sport", logoUrl: "https://img.example/sport.png");
        await SeedProgramAsync(db, news.ChannelId, "Current News", nowUtc.AddMinutes(-15), nowUtc.AddMinutes(15));
        await SeedProgramAsync(db, news.ChannelId, "Next News", nowUtc.AddMinutes(15), nowUtc.AddMinutes(45));
        await SeedProgramAsync(db, sport.ChannelId, "Current Match", nowUtc.AddMinutes(-15), nowUtc.AddMinutes(15));
        await SeedProgramAsync(db, sport.ChannelId, "Next Match", nowUtc.AddMinutes(15), nowUtc.AddMinutes(45));
        await SeedEpgLogAsync(db, sourceId, EpgStatus.Ready, EpgSyncResultCode.Ready, xmltvChannels: 2, programmes: 4, matched: 2);
        await SeedSyncStateAsync(db, sourceId, 200, "OK");
        await SeedAcquisitionRunAsync(db, sourceId, SourceAcquisitionRunStatus.Succeeded, live: 2);

        var snapshot = await RefreshAndSnapshotAsync(db, sourceId);

        Assert.AreEqual("Healthy", snapshot.HealthLabel);
        Assert.AreEqual(2, snapshot.ProgrammeBackedChannelCount);
        Assert.AreEqual(2, snapshot.CurrentCoverageCount);
        Assert.AreEqual(2, snapshot.NextCoverageCount);
        Assert.IsTrue(snapshot.RecommendedActions.Any(action => action.ActionType == SourceRecommendedActionType.RunStreamProbe));
        Assert.IsTrue(snapshot.RecommendedActions.Any(action => action.ActionType == SourceRecommendedActionType.ExportDiagnostics));
    }

    [TestMethod]
    public async Task SourceWithoutEpg_RecommendsConfigureEpg()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "No Guide Source", lastSyncUtc: DateTime.UtcNow.AddMinutes(-20));
        await SeedChannelAsync(db, sourceId, "News", "World News", "https://stream.example/news.m3u8", "news", logoUrl: "https://img.example/news.png");
        await SeedSyncStateAsync(db, sourceId, 200, "OK");

        var snapshot = await RefreshAndSnapshotAsync(db, sourceId);

        Assert.AreEqual("Incomplete", snapshot.HealthLabel);
        Assert.IsFalse(snapshot.HasEpgUrl);
        Assert.AreEqual("No XMLTV URL discovered.", snapshot.EpgDiscoveryText);
        Assert.IsTrue(snapshot.RecommendedActions.Any(action => action.ActionType == SourceRecommendedActionType.ConfigureEpg));
    }

    [TestMethod]
    public async Task EpgWithoutProgrammes_ReportsXmltvOnlyAndManualMatchAction()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Empty XMLTV Source", lastSyncUtc: DateTime.UtcNow.AddMinutes(-20), detectedEpgUrl: "https://guide.example/empty.xml");
        await SeedChannelAsync(db, sourceId, "News", "World News", "https://stream.example/news.m3u8", "news", logoUrl: "https://img.example/news.png");
        await SeedEpgLogAsync(db, sourceId, EpgStatus.Ready, EpgSyncResultCode.ZeroCoverage, xmltvChannels: 3, programmes: 0, matched: 0);
        await SeedSyncStateAsync(db, sourceId, 200, "OK");

        var snapshot = await RefreshAndSnapshotAsync(db, sourceId);

        Assert.AreEqual(3, snapshot.XmltvChannelCount);
        Assert.AreEqual(0, snapshot.EpgProgramCount);
        Assert.AreEqual(0, snapshot.ProgrammeBackedChannelCount);
        Assert.IsTrue(snapshot.RecommendedActions.Any(action => action.ActionType == SourceRecommendedActionType.OpenManualEpgMatch));
    }

    [TestMethod]
    public async Task StaleSource_ReportsOutdatedAndStaleGuide()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var nowUtc = DateTime.UtcNow;
        var sourceId = await SeedSourceAsync(db, "Stale Source", lastSyncUtc: nowUtc.AddDays(-8), detectedEpgUrl: "https://guide.example/xmltv.xml");
        var channel = await SeedChannelAsync(db, sourceId, "News", "World News", "https://stream.example/news.m3u8", "news", logoUrl: "https://img.example/news.png");
        await SeedProgramAsync(db, channel.ChannelId, "Current News", nowUtc.AddMinutes(-15), nowUtc.AddMinutes(15));
        await SeedProgramAsync(db, channel.ChannelId, "Next News", nowUtc.AddMinutes(15), nowUtc.AddMinutes(45));
        await SeedEpgLogAsync(db, sourceId, EpgStatus.Stale, EpgSyncResultCode.Ready, xmltvChannels: 1, programmes: 2, matched: 1);
        await SeedSyncStateAsync(db, sourceId, 200, "OK");

        var snapshot = await RefreshAndSnapshotAsync(db, sourceId);

        Assert.AreEqual("Outdated", snapshot.HealthLabel);
        Assert.IsTrue(snapshot.IsGuideStale);
        Assert.IsTrue(snapshot.RecommendedActions.Any(action => action.ActionType == SourceRecommendedActionType.ResyncSource));
    }

    [TestMethod]
    public async Task DuplicateHeavySource_ScoresWeakAndCountsDuplicates()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Duplicate Source", lastSyncUtc: DateTime.UtcNow.AddMinutes(-20), epgMode: EpgActiveMode.None);
        for (var index = 0; index < 10; index++)
        {
            await SeedChannelAsync(db, sourceId, "News", $"Duplicate {index}", "https://stream.example/shared.m3u8", $"dup{index}", logoUrl: $"https://img.example/{index}.png");
        }
        await SeedSyncStateAsync(db, sourceId, 200, "OK");

        var snapshot = await RefreshAndSnapshotAsync(db, sourceId);

        Assert.AreEqual("Weak", snapshot.HealthLabel);
        Assert.IsTrue(snapshot.DuplicateCount >= 9);
    }

    [TestMethod]
    public async Task InvalidHeavySource_ScoresProblematicAndCountsInvalidStreams()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Invalid Source", lastSyncUtc: DateTime.UtcNow.AddMinutes(-20), epgMode: EpgActiveMode.None);
        for (var index = 0; index < 5; index++)
        {
            await SeedChannelAsync(db, sourceId, "News", $"Broken {index}", $"not a url {index}", $"broken{index}", logoUrl: $"https://img.example/{index}.png");
        }
        await SeedSyncStateAsync(db, sourceId, 200, "OK");

        var snapshot = await RefreshAndSnapshotAsync(db, sourceId);

        Assert.AreEqual("Problematic", snapshot.HealthLabel);
        Assert.AreEqual(5, snapshot.InvalidStreamCount);
        Assert.IsTrue(snapshot.RecommendedActions.Any(action => action.ActionType == SourceRecommendedActionType.RunStreamProbe));
    }

    [TestMethod]
    public async Task FailedAuth_ReportsProblematicAndRedactsSecrets()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Failed Auth Source", SourceType.Xtream, lastSyncUtc: null, detectedEpgUrl: "https://xtream.example/xmltv.php?username=alice&password=secret");
        await SeedSyncStateAsync(db, sourceId, 401, "Auth failed for username=alice password=secret at https://xtream.example/player_api.php?username=alice&password=secret");

        var snapshot = await RefreshAndSnapshotAsync(db, sourceId);

        Assert.AreEqual("Problematic", snapshot.HealthLabel);
        Assert.IsFalse(snapshot.FailureReasonText.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(snapshot.FailureReasonText.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(snapshot.SafeDiagnosticsReportText.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(snapshot.RecommendedActions.Any(action => action.ActionType == SourceRecommendedActionType.RemoveSource));
    }

    [TestMethod]
    public async Task PartialImport_TracksUnknownClassificationAndResyncAction()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Partial Source", lastSyncUtc: DateTime.UtcNow.AddMinutes(-20), epgMode: EpgActiveMode.None);
        await SeedChannelAsync(db, sourceId, "News", "World News", "https://stream.example/news.m3u8", "news", logoUrl: "https://img.example/news.png");
        await SeedSyncStateAsync(db, sourceId, 200, "partial import");
        await SeedAcquisitionRunAsync(db, sourceId, SourceAcquisitionRunStatus.Partial, live: 1, unmatched: 7, message: "7 entries could not be classified safely.");

        var snapshot = await RefreshAndSnapshotAsync(db, sourceId);

        Assert.AreEqual(7, snapshot.UnknownClassificationCount);
        Assert.IsTrue(snapshot.RecommendedActions.Any(action => action.ActionType == SourceRecommendedActionType.ResyncSource));
    }

    private static async Task<SourceDiagnosticsSnapshot> RefreshAndSnapshotAsync(AppDbContext db, int sourceId)
    {
        var health = CreateHealthService();
        await health.RefreshSourceHealthAsync(db, sourceId, forceProbe: true);
        db.ChangeTracker.Clear();

        var diagnostics = new SourceDiagnosticsService(
            health,
            new NoopSourceAcquisitionService(),
            new SensitiveDataRedactionService());
        var snapshots = await diagnostics.GetSnapshotsAsync(db, new[] { sourceId });
        return snapshots[sourceId];
    }

    private static SourceHealthService CreateHealthService()
    {
        return new SourceHealthService(
            new FixtureSourceProbeService(),
            new SourceRoutingService(),
            new PassthroughProviderStreamResolver());
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

    private static async Task<int> SeedSourceAsync(
        AppDbContext db,
        string name,
        SourceType type = SourceType.M3U,
        DateTime? lastSyncUtc = null,
        string detectedEpgUrl = "",
        EpgActiveMode epgMode = EpgActiveMode.Detected)
    {
        var source = new SourceProfile
        {
            Name = name,
            Type = type,
            LastSync = lastSyncUtc
        };
        db.SourceProfiles.Add(source);
        await db.SaveChangesAsync();

        db.SourceCredentials.Add(new SourceCredential
        {
            SourceProfileId = source.Id,
            Url = type == SourceType.Xtream ? "https://xtream.example" : "https://playlist.example/source.m3u",
            Username = type == SourceType.Xtream ? "alice" : string.Empty,
            Password = type == SourceType.Xtream ? "secret" : string.Empty,
            DetectedEpgUrl = detectedEpgUrl,
            EpgMode = epgMode
        });
        await db.SaveChangesAsync();
        return source.Id;
    }

    private static async Task<ChannelSeedResult> SeedChannelAsync(
        AppDbContext db,
        int sourceId,
        string categoryName,
        string channelName,
        string streamUrl,
        string epgId,
        string logoUrl = "")
    {
        var category = await db.ChannelCategories.FirstOrDefaultAsync(item => item.SourceProfileId == sourceId && item.Name == categoryName);
        if (category == null)
        {
            category = new ChannelCategory
            {
                SourceProfileId = sourceId,
                Name = categoryName,
                OrderIndex = 0
            };
            db.ChannelCategories.Add(category);
            await db.SaveChangesAsync();
        }

        var channel = new Channel
        {
            ChannelCategoryId = category.Id,
            Name = channelName,
            StreamUrl = streamUrl,
            LogoUrl = logoUrl,
            ProviderEpgChannelId = epgId,
            EpgChannelId = epgId,
            EpgMatchSource = string.IsNullOrWhiteSpace(epgId) ? ChannelEpgMatchSource.None : ChannelEpgMatchSource.Provider,
            EpgMatchConfidence = string.IsNullOrWhiteSpace(epgId) ? 0 : 98
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        return new ChannelSeedResult(category.Id, channel.Id);
    }

    private static async Task SeedProgramAsync(AppDbContext db, int channelId, string title, DateTime startUtc, DateTime endUtc)
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

    private static async Task SeedEpgLogAsync(
        AppDbContext db,
        int sourceId,
        EpgStatus status,
        EpgSyncResultCode resultCode,
        int xmltvChannels,
        int programmes,
        int matched)
    {
        db.EpgSyncLogs.Add(new EpgSyncLog
        {
            SourceProfileId = sourceId,
            SyncedAtUtc = DateTime.UtcNow,
            LastSuccessAtUtc = resultCode == EpgSyncResultCode.ParseFailed ? null : DateTime.UtcNow,
            IsSuccess = status is EpgStatus.Ready or EpgStatus.ManualOverride or EpgStatus.Stale,
            Status = status,
            ResultCode = resultCode,
            ActiveMode = EpgActiveMode.Detected,
            ActiveXmltvUrl = "https://guide.example/xmltv.xml",
            XmltvChannelCount = xmltvChannels,
            ProgrammeCount = programmes,
            MatchedChannelCount = matched,
            CurrentCoverageCount = matched,
            NextCoverageCount = matched,
            TotalLiveChannelCount = matched
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSyncStateAsync(AppDbContext db, int sourceId, int httpStatusCode, string errorLog)
    {
        db.SourceSyncStates.Add(new SourceSyncState
        {
            SourceProfileId = sourceId,
            LastAttempt = DateTime.UtcNow,
            HttpStatusCode = httpStatusCode,
            ErrorLog = errorLog
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAcquisitionRunAsync(
        AppDbContext db,
        int sourceId,
        SourceAcquisitionRunStatus status,
        int live = 0,
        int movies = 0,
        int series = 0,
        int episodes = 0,
        int unmatched = 0,
        string message = "OK")
    {
        db.SourceAcquisitionRuns.Add(new SourceAcquisitionRun
        {
            SourceProfileId = sourceId,
            Trigger = SourceRefreshTrigger.Manual,
            Scope = SourceRefreshScope.Full,
            Status = status,
            StartedAtUtc = DateTime.UtcNow.AddSeconds(-12),
            CompletedAtUtc = DateTime.UtcNow,
            ProfileKey = "fixture",
            ProfileLabel = "Fixture",
            ProviderKey = "fixture-provider",
            RoutingSummary = "Direct routing",
            ValidationRoutingSummary = "Direct routing",
            Message = message,
            CatalogSummary = "Fixture catalog",
            GuideSummary = "Fixture guide",
            ValidationSummary = "Fixture validation",
            RawItemCount = live + movies + series + episodes + unmatched,
            AcceptedCount = live + movies + series + episodes,
            UnmatchedCount = unmatched,
            LiveCount = live,
            MovieCount = movies,
            SeriesCount = series,
            EpisodeCount = episodes
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixtureSourceProbeService : ISourceProbeService
    {
        public Task<SourceProbeRunResult> ProbeAsync(
            SourceHealthProbeType probeType,
            IReadOnlyList<SourceProbeCandidate> candidates,
            SourceRoutingDecision? routing = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var probeable = candidates
                .Where(candidate => Uri.TryCreate(candidate.StreamUrl, UriKind.Absolute, out var uri) &&
                                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                .Take(4)
                .ToList();
            if (probeable.Count == 0)
            {
                return Task.FromResult(new SourceProbeRunResult
                {
                    Status = SourceHealthProbeStatus.Skipped,
                    CandidateCount = 0,
                    Summary = "No HTTP sample was available for probing."
                });
            }

            return Task.FromResult(new SourceProbeRunResult
            {
                Status = SourceHealthProbeStatus.Completed,
                ProbedAtUtc = DateTime.UtcNow,
                CandidateCount = candidates.Count,
                SampleSize = probeable.Count,
                SuccessCount = probeable.Count,
                Summary = $"{probeType} probe reached {probeable.Count}/{probeable.Count} sampled items."
            });
        }
    }

    private sealed class PassthroughProviderStreamResolver : IProviderStreamResolverService
    {
        public Task<ProviderStreamResolution> ResolveAsync(
            AppDbContext db,
            int preferredSourceProfileId,
            string catalogStreamUrl,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(catalogStreamUrl))
            {
                return Task.FromResult(ProviderStreamResolution.Failed("Missing stream URL."));
            }

            var routing = new SourceRoutingDecision { Summary = "Direct routing" };
            return Task.FromResult(ProviderStreamResolution.CreateSuccess(
                catalogStreamUrl,
                catalogStreamUrl,
                catalogStreamUrl,
                "Fixture stream",
                routing,
                routing,
                new CompanionRelayDecision(),
                "Direct routing"));
        }

        public Task<ProviderStreamResolution> ResolvePlaybackContextAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            return ResolveAsync(db, context.PreferredSourceProfileId, context.StreamUrl, purpose, cancellationToken);
        }
    }

    private sealed class NoopSourceAcquisitionService : ISourceAcquisitionService
    {
        public Task<SourceAcquisitionSession> BeginSessionAsync(
            AppDbContext db,
            SourceProfile profile,
            SourceCredential? credential,
            SourceRefreshTrigger trigger,
            SourceRefreshScope scope)
        {
            throw new NotSupportedException();
        }

        public Task CompleteSessionAsync(
            AppDbContext db,
            SourceAcquisitionSession session,
            SourceAcquisitionRunStatus status,
            string message,
            string catalogSummary,
            string guideSummary,
            string validationSummary)
        {
            return Task.CompletedTask;
        }

        public Task BackfillAsync(
            AppDbContext db,
            IReadOnlyCollection<int>? sourceIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record ChannelSeedResult(int CategoryId, int ChannelId);
}

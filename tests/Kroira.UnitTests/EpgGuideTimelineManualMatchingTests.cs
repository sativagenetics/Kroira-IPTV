using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class EpgGuideTimelineManualMatchingTests
{
    [TestMethod]
    public void TimelineSlotGeneration_UsesRequestedRangeAndSlotSize()
    {
        var start = new DateTime(2030, 1, 2, 12, 0, 0, DateTimeKind.Utc);

        var slots = EpgGuideTimelineService.GenerateSlots(start, start.AddHours(2), TimeSpan.FromMinutes(30));

        Assert.AreEqual(4, slots.Count);
        Assert.AreEqual(start, slots[0].StartUtc);
        Assert.AreEqual(start.AddMinutes(30), slots[0].EndUtc);
        Assert.AreEqual(start.AddMinutes(90), slots[3].StartUtc);
    }

    [TestMethod]
    public async Task CurrentNextLookup_ReturnsBatchedProgrammeSummaries()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var nowUtc = new DateTime(2030, 1, 2, 12, 15, 0, DateTimeKind.Utc);
        var channel = await SeedChannelAsync(db, "Fixture Source", "News", "World News", "news");
        await SeedProgramAsync(db, channel.ChannelId, "Current Bulletin", nowUtc.AddMinutes(-15), nowUtc.AddMinutes(15));
        await SeedProgramAsync(db, channel.ChannelId, "Next Bulletin", nowUtc.AddMinutes(15), nowUtc.AddMinutes(45));

        var result = await new EpgGuideTimelineService().BuildTimelineAsync(
            db,
            new EpgGuideTimelineRequest
            {
                RangeStartUtc = new DateTime(2030, 1, 2, 12, 0, 0, DateTimeKind.Utc),
                RangeDuration = TimeSpan.FromHours(2),
                SlotDuration = TimeSpan.FromMinutes(30),
                NowUtc = nowUtc
            });

        Assert.AreEqual(1, result.Channels.Count);
        Assert.AreEqual("Current Bulletin", result.Channels[0].CurrentProgram?.Title);
        Assert.AreEqual("Next Bulletin", result.Channels[0].NextProgram?.Title);
        Assert.AreEqual(2, result.Channels[0].Programs.Count);
        Assert.IsTrue(result.HasGuideData);
    }

    [TestMethod]
    public async Task TimelineFilters_ApplySourceCategoryAndSearch()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var nowUtc = new DateTime(2030, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var news = await SeedChannelAsync(db, "News Source", "News", "World News", "news");
        var sports = await SeedChannelAsync(db, "Sports Source", "Sports", "Match Arena", "match");
        await SeedProgramAsync(db, news.ChannelId, "News Hour", nowUtc, nowUtc.AddHours(1));
        await SeedProgramAsync(db, sports.ChannelId, "Live Match", nowUtc, nowUtc.AddHours(1));
        var service = new EpgGuideTimelineService();

        var sourceFiltered = await service.BuildTimelineAsync(
            db,
            BuildRequest(nowUtc, sourceProfileId: news.SourceProfileId));
        var categoryFiltered = await service.BuildTimelineAsync(
            db,
            BuildRequest(nowUtc, categoryId: sports.CategoryId));
        var searchFiltered = await service.BuildTimelineAsync(
            db,
            BuildRequest(nowUtc, searchText: "World"));

        Assert.AreEqual(1, sourceFiltered.Channels.Count);
        Assert.AreEqual("World News", sourceFiltered.Channels[0].ChannelName);
        Assert.AreEqual(1, categoryFiltered.Channels.Count);
        Assert.AreEqual("Match Arena", categoryFiltered.Channels[0].ChannelName);
        Assert.AreEqual(1, searchFiltered.Channels.Count);
        Assert.AreEqual("World News", searchFiltered.Channels[0].ChannelName);
    }

    [TestMethod]
    public async Task StaleGuideDetection_FlagsOldSuccessfulGuide()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var nowUtc = new DateTime(2030, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var channel = await SeedChannelAsync(db, "Old Guide Source", "News", "World News", "news");
        await SeedProgramAsync(db, channel.ChannelId, "Old Show", nowUtc.AddHours(-2), nowUtc.AddHours(-1));
        db.EpgSyncLogs.Add(new EpgSyncLog
        {
            SourceProfileId = channel.SourceProfileId,
            SyncedAtUtc = nowUtc.AddHours(-48),
            LastSuccessAtUtc = nowUtc.AddHours(-48),
            IsSuccess = true,
            Status = EpgStatus.Ready
        });
        await db.SaveChangesAsync();

        var result = await new EpgGuideTimelineService().BuildTimelineAsync(db, BuildRequest(nowUtc));

        Assert.IsTrue(result.IsStale);
        StringAssert.Contains(result.StaleWarningText, "Old Guide Source");
    }

    [TestMethod]
    public void ManualOverridePrecedence_BeatsAutomaticProviderMatch()
    {
        var identity = new LiveChannelIdentityService();
        var automaticProviderChannel = BuildChannel(identity, 1, "Provider Channel", "auto", "https://stream.example/auto.m3u8");
        var manuallyApprovedChannel = BuildChannel(identity, 2, "Manual Target", "manual", "https://stream.example/manual.m3u8");
        var decisions = new[]
        {
            new EpgMappingDecision
            {
                SourceProfileId = 42,
                ChannelId = manuallyApprovedChannel.Id,
                XmltvChannelId = "auto",
                XmltvDisplayName = "Auto XMLTV",
                Decision = EpgMappingDecisionState.Approved,
                SuggestedMatchSource = ChannelEpgMatchSource.UserApproved,
                SuggestedConfidence = 93
            }
        };
        var xmltvChannels = new[]
        {
            new XmltvChannelDescriptor
            {
                Id = "auto",
                DisplayNames = new[] { "Provider Channel" }
            }
        };

        var outcomes = SourceEnrichmentService.MatchXmltvChannels(
            identity,
            new[] { automaticProviderChannel, manuallyApprovedChannel },
            xmltvChannels,
            decisions,
            sourceProfileId: 42);

        Assert.AreEqual(ChannelEpgMatchSource.UserApproved, outcomes["auto"].Reason);
        Assert.AreEqual(manuallyApprovedChannel.Id, outcomes["auto"].Channels.Single().Id);
    }

    private static EpgGuideTimelineRequest BuildRequest(
        DateTime nowUtc,
        int? sourceProfileId = null,
        int? categoryId = null,
        string searchText = "")
    {
        return new EpgGuideTimelineRequest
        {
            SourceProfileId = sourceProfileId,
            CategoryId = categoryId,
            SearchText = searchText,
            RangeStartUtc = nowUtc,
            RangeDuration = TimeSpan.FromHours(2),
            SlotDuration = TimeSpan.FromMinutes(30),
            NowUtc = nowUtc
        };
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

    private static async Task<ChannelSeedResult> SeedChannelAsync(
        AppDbContext db,
        string sourceName,
        string categoryName,
        string channelName,
        string epgChannelId)
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

        var channel = new Channel
        {
            ChannelCategoryId = category.Id,
            Name = channelName,
            StreamUrl = $"https://stream.example/{source.Id}/{epgChannelId}.m3u8",
            ProviderEpgChannelId = epgChannelId,
            EpgChannelId = epgChannelId,
            EpgMatchSource = ChannelEpgMatchSource.Provider,
            EpgMatchConfidence = 97
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        return new ChannelSeedResult(source.Id, category.Id, channel.Id);
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

    private static Channel BuildChannel(
        ILiveChannelIdentityService identityService,
        int id,
        string name,
        string providerEpgChannelId,
        string streamUrl)
    {
        var identity = identityService.Build(name, providerEpgChannelId);
        return new Channel
        {
            Id = id,
            Name = name,
            StreamUrl = streamUrl,
            ProviderEpgChannelId = providerEpgChannelId,
            NormalizedIdentityKey = identity.IdentityKey,
            NormalizedName = identity.NormalizedName,
            AliasKeys = string.Join('\n', identity.AliasKeys)
        };
    }

    private sealed record ChannelSeedResult(int SourceProfileId, int CategoryId, int ChannelId);
}

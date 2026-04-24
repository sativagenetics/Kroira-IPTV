using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Parsing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class EpgDiscoveryParsingMatchingTests
{
    [TestMethod]
    public void M3uHeaderMetadata_ParsesDiscoveryKeysQuotedUnquotedAndMultipleUrls()
    {
        const string playlist = """
            #EXTM3U x-tvg-url="https://one.example/guide.xml, https://two.example/guide.xml" url-tvg=https://three.example/guide.xml tvg-url=relative.xml
            #EXTM3U epg-url='https://four.example/guide.xml' xmltv=https://five.example/guide.xml
            #EXTINF:-1 tvg-id="news",News
            http://stream.example/news.m3u8
            """;

        var metadata = M3uMetadataParser.ParseHeaderMetadata(playlist, "https://playlist.example/path/list.m3u");

        CollectionAssert.AreEquivalent(
            new[]
            {
                "https://one.example/guide.xml",
                "https://two.example/guide.xml",
                "https://three.example/guide.xml",
                "https://playlist.example/path/relative.xml",
                "https://four.example/guide.xml",
                "https://five.example/guide.xml"
            },
            metadata.XmltvUrls.ToArray());
    }

    [TestMethod]
    public void EpgDiagnostics_RedactsCredentialsInGuideUrls()
    {
        var formatted = EpgDiagnosticFormatter.Format(
            "https://user:pass@example.invalid/xmltv.php?username=alice&password=secret&token=abcdef");

        Assert.IsFalse(formatted.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(formatted.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(formatted.Contains("user:pass", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(formatted.Contains("abcdef", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(formatted, "***");
    }

    [TestMethod]
    public void XtreamDiscovery_DerivesXmltvEndpointAndConfiguredFallback()
    {
        var credential = new SourceCredential
        {
            Url = "https://xtream.example:8080/player_api.php?username=old&password=old",
            Username = "alice",
            Password = "secret",
            ManualEpgUrl = "https://fallback.example/guide.xml"
        };

        var derived = EpgDiscoveryHelpers.BuildXtreamProviderXmltvUrl(credential);
        var fallbacks = EpgDiscoveryHelpers.BuildConfiguredEpgUrlFallbackCandidates(credential, 7);

        Assert.AreEqual("https://xtream.example:8080/xmltv.php?username=alice&password=secret", derived);
        Assert.AreEqual(1, fallbacks.Count);
        Assert.AreEqual("https://fallback.example/guide.xml", fallbacks[0].Url);
        Assert.AreEqual("configured_epg_url", fallbacks[0].Method);
        Assert.AreEqual(EpgGuideSourceKind.Manual, fallbacks[0].Kind);
        Assert.IsTrue(fallbacks[0].IsOptional);
        Assert.AreEqual(7, fallbacks[0].Priority);
    }

    [TestMethod]
    public async Task XmltvImport_ImportsProgrammeFieldsAndChannelIcon()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceProfileId = await SeedSourceAsync(db, new ChannelSeed("News Channel", "news"));
        var xml = """
            <tv>
              <channel id="news">
                <display-name>News Channel</display-name>
                <icon src="https://images.example/news.png" />
              </channel>
              <programme channel="news" start="20300102090000 +0000" stop="20300102100000 +0000">
                <title>Morning News</title>
                <sub-title>Headlines</sub-title>
                <desc>Daily briefing.</desc>
                <category>News</category>
              </programme>
            </tv>
            """;

        await ImportXmlAsync(db, sourceProfileId, xml);

        var program = await db.EpgPrograms.SingleAsync();
        var channel = await db.Channels.SingleAsync();
        Assert.AreEqual("Morning News", program.Title);
        Assert.AreEqual("Headlines", program.Subtitle);
        Assert.AreEqual("Daily briefing.", program.Description);
        Assert.AreEqual("News", program.Category);
        Assert.AreEqual(new DateTime(2030, 1, 2, 9, 0, 0, DateTimeKind.Utc), program.StartTimeUtc);
        Assert.AreEqual(new DateTime(2030, 1, 2, 10, 0, 0, DateTimeKind.Utc), program.EndTimeUtc);
        Assert.AreEqual("https://images.example/news.png", channel.LogoUrl);
        Assert.AreEqual(ChannelLogoSource.Xmltv, channel.LogoSource);
    }

    [TestMethod]
    public async Task XmltvImport_DottedIdsMatchExactlyAndArePreserved()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceProfileId = await SeedSourceAsync(
            db,
            new ChannelSeed("beIN 1", "bein1.tr"),
            new ChannelSeed("Repair News", "repair.news"));
        var xml = """
            <tv>
              <channel id="bein1.tr"><display-name>beIN 1</display-name></channel>
              <channel id="repair.news"><display-name>Repair News</display-name></channel>
              <programme channel="bein1.tr" start="20300102090000 +0000" stop="20300102100000 +0000"><title>Match</title></programme>
              <programme channel="repair.news" start="20300102090000 +0000" stop="20300102100000 +0000"><title>Update</title></programme>
            </tv>
            """;

        await ImportXmlAsync(db, sourceProfileId, xml);

        var channels = await db.Channels.OrderBy(channel => channel.ProviderEpgChannelId).ToListAsync();
        CollectionAssert.AreEquivalent(new[] { "bein1.tr", "repair.news" }, channels.Select(channel => channel.EpgChannelId).ToArray());
        Assert.IsTrue(channels.All(channel => channel.EpgMatchSource == ChannelEpgMatchSource.Provider));
        Assert.AreEqual(2, await db.EpgPrograms.CountAsync());
    }

    [TestMethod]
    public async Task XmltvImport_TrustedAliasesImportProgrammes()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceProfileId = await SeedSourceAsync(db, new ChannelSeed("BBC One"));
        var xml = """
            <tv>
              <channel id="bbc.one.uk"><display-name>BBC 1</display-name></channel>
              <programme channel="bbc.one.uk" start="20300102090000 +0000" stop="20300102100000 +0000"><title>Breakfast</title></programme>
            </tv>
            """;

        await ImportXmlAsync(db, sourceProfileId, xml);

        var channel = await db.Channels.SingleAsync();
        Assert.AreEqual(ChannelEpgMatchSource.Normalized, channel.EpgMatchSource);
        Assert.AreEqual("bbc.one.uk", channel.EpgChannelId);
        Assert.AreEqual(1, await db.EpgPrograms.CountAsync());
    }

    [TestMethod]
    public async Task XmltvImport_MalformedXmlFailsSafely()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceProfileId = await SeedSourceAsync(db, new ChannelSeed("News", "news"));
        const string xml = "<tv><channel id=\"news\"><display-name>News</display-name>";

        await Assert.ThrowsExceptionAsync<Exception>(() => ImportXmlAsync(db, sourceProfileId, xml));

        var log = await db.EpgSyncLogs.SingleAsync();
        Assert.IsFalse(log.IsSuccess);
        Assert.AreEqual(EpgStatus.FailedFetchOrParse, log.Status);
        Assert.AreEqual(EpgSyncResultCode.ParseFailed, log.ResultCode);
        Assert.AreEqual(EpgFailureStage.Parse, log.FailureStage);
        Assert.AreEqual(0, await db.EpgPrograms.CountAsync());
    }

    [TestMethod]
    public async Task XmltvImport_TimezoneOffsetsNormalizeToUtc()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceProfileId = await SeedSourceAsync(db, new ChannelSeed("Timezone", "timezone"));
        var xml = """
            <tv>
              <channel id="timezone"><display-name>Timezone</display-name></channel>
              <programme channel="timezone" start="20300102090000 +0300" stop="20300102100000 +0300"><title>Offset Show</title></programme>
            </tv>
            """;

        await ImportXmlAsync(db, sourceProfileId, xml);

        var program = await db.EpgPrograms.SingleAsync();
        Assert.AreEqual(new DateTime(2030, 1, 2, 6, 0, 0, DateTimeKind.Utc), program.StartTimeUtc);
        Assert.AreEqual(new DateTime(2030, 1, 2, 7, 0, 0, DateTimeKind.Utc), program.EndTimeUtc);
    }

    [TestMethod]
    public async Task XmltvImport_NoProgrammesDoesNotCountDeclarationsOrMatchesAsCoverage()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceProfileId = await SeedSourceAsync(db, new ChannelSeed("News", "news"));
        var xml = """
            <tv>
              <channel id="news"><display-name>News</display-name></channel>
            </tv>
            """;

        await ImportXmlAsync(db, sourceProfileId, xml);

        var log = await db.EpgSyncLogs.SingleAsync();
        Assert.AreEqual(0, await db.EpgPrograms.CountAsync());
        Assert.AreEqual(1, log.XmltvChannelCount);
        Assert.AreEqual(0, log.MatchedChannelCount);
        Assert.AreEqual(1, log.UnmatchedChannelCount);
        Assert.AreEqual(0, log.ProgrammeCount);
        Assert.AreEqual(0, log.CurrentCoverageCount);
        Assert.AreEqual(0, log.NextCoverageCount);
        Assert.AreEqual(EpgSyncResultCode.ZeroCoverage, log.ResultCode);
    }

    [TestMethod]
    public async Task XmltvImport_WeakFuzzyMatchesAreReviewOnly()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceProfileId = await SeedSourceAsync(db, new ChannelSeed("Discovery National Geographic"));
        var xml = """
            <tv>
              <channel id="natgeo.fixture"><display-name>Discovery National Geographicc</display-name></channel>
              <programme channel="natgeo.fixture" start="20300102090000 +0000" stop="20300102100000 +0000"><title>Documentary</title></programme>
            </tv>
            """;

        await ImportXmlAsync(db, sourceProfileId, xml);

        var channel = await db.Channels.SingleAsync();
        var log = await db.EpgSyncLogs.SingleAsync();
        Assert.AreEqual(ChannelEpgMatchSource.Fuzzy, channel.EpgMatchSource);
        Assert.AreEqual(0, await db.EpgPrograms.CountAsync());
        Assert.AreEqual(0, log.MatchedChannelCount);
        Assert.AreEqual(1, log.UnmatchedChannelCount);
        Assert.AreEqual(1, log.WeakMatchCount);
    }

    [TestMethod]
    public async Task XmltvImport_CoverageMetricsCountOnlyProgrammeBackedSourceChannels()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceProfileId = await SeedSourceAsync(
            db,
            new ChannelSeed("Channel A", "a"),
            new ChannelSeed("Channel B", "b"),
            new ChannelSeed("Channel C", "c"));
        var xml = """
            <tv>
              <channel id="a"><display-name>Channel A</display-name></channel>
              <channel id="b"><display-name>Channel B</display-name></channel>
              <programme channel="a" start="20300102090000 +0000" stop="20300102100000 +0000"><title>A Show</title></programme>
            </tv>
            """;

        await ImportXmlAsync(db, sourceProfileId, xml);

        var log = await db.EpgSyncLogs.SingleAsync();
        Assert.AreEqual(2, log.XmltvChannelCount);
        Assert.AreEqual(1, log.ProgrammeCount);
        Assert.AreEqual(1, log.MatchedChannelCount);
        Assert.AreEqual(2, log.UnmatchedChannelCount);
        Assert.AreEqual(1, log.NextCoverageCount);
        Assert.AreEqual(EpgSyncResultCode.PartialMatch, log.ResultCode);
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

    private static async Task<int> SeedSourceAsync(AppDbContext db, params ChannelSeed[] channels)
    {
        var profile = new SourceProfile
        {
            Name = "Fixture Source",
            Type = SourceType.M3U
        };
        db.SourceProfiles.Add(profile);
        await db.SaveChangesAsync();

        db.SourceCredentials.Add(new SourceCredential
        {
            SourceProfileId = profile.Id,
            Url = "https://playlist.example/list.m3u",
            EpgMode = EpgActiveMode.Detected
        });
        var category = new ChannelCategory
        {
            SourceProfileId = profile.Id,
            Name = "Live",
            OrderIndex = 0
        };
        db.ChannelCategories.Add(category);
        await db.SaveChangesAsync();

        for (var index = 0; index < channels.Length; index++)
        {
            var seed = channels[index];
            db.Channels.Add(new Channel
            {
                ChannelCategoryId = category.Id,
                Name = seed.Name,
                StreamUrl = string.IsNullOrWhiteSpace(seed.StreamUrl)
                    ? $"https://stream.example/live/{index + 1}.m3u8"
                    : seed.StreamUrl,
                ProviderEpgChannelId = seed.ProviderEpgId,
                EpgChannelId = seed.ProviderEpgId
            });
        }

        await db.SaveChangesAsync();
        return profile.Id;
    }

    private static async Task ImportXmlAsync(AppDbContext db, int sourceProfileId, string xml)
    {
        var service = new XmltvParserService(
            new IEpgSourceDiscoveryService[] { new FixtureDiscoveryService(xml) },
            new SourceEnrichmentService(new LiveChannelIdentityService()),
            new NoopSourceHealthService());
        await service.ParseAndImportEpgAsync(db, sourceProfileId, refreshHealth: false);
    }

    private sealed record ChannelSeed(string Name, string ProviderEpgId = "", string StreamUrl = "");

    private sealed class FixtureDiscoveryService : IEpgSourceDiscoveryService
    {
        private readonly string _xml;

        public FixtureDiscoveryService(string xml)
        {
            _xml = xml;
        }

        public SourceType SourceType => SourceType.M3U;

        public Task<EpgDiscoveryResult> DiscoverAsync(AppDbContext db, int sourceProfileId)
        {
            var source = new EpgDiscoveredGuideSource
            {
                Label = "Fixture XMLTV",
                Url = "https://user:pass@example.invalid/xmltv.php?username=alice&password=secret",
                Kind = EpgGuideSourceKind.Provider,
                Status = EpgGuideSourceStatus.Ready,
                Priority = 0,
                XmlContent = _xml,
                CheckedAtUtc = DateTime.UtcNow
            };
            return Task.FromResult(new EpgDiscoveryResult(
                new[] { source },
                "Fixture XMLTV",
                source.Url,
                EpgActiveMode.Detected));
        }
    }

    private sealed class NoopSourceHealthService : ISourceHealthService
    {
        public Task RefreshSourceHealthAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null)
        {
            return Task.CompletedTask;
        }
    }
}

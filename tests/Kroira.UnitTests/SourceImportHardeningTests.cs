using System.Net;
using System.Net.Http;
using System.Text;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Parsing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class SourceImportHardeningTests
{
    [TestMethod]
    public async Task M3uImport_DirtyPlaylistParsesAttributesFiltersNoiseAndDiscoversEpg()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var playlistPath = await WritePlaylistAsync("""
            ﻿#EXTM3U x-tvg-url="https://guide.example/a.xml, https://guide.example/b.xml"
            # Provider comment
            #EXTINF:-1 TVG-ID="live.one" tvg-name="Live \"One\"" GROUP-TITLE=" *** News | " logo="https://img.example/logo.png",Live One
            #KODIPROP:inputstream=inputstream.adaptive
            http://stream.example/live/one.ts?token=abc&foo=bar
            #EXTINF:-1 tvg-id=live.two group-title=Live tvg-logo=http://logo.example/two.png Live Two
            http://stream.example/live/two.m3u8?token=abc
            #EXTINF:-1 tvg-id=live.two group-title=Live tvg-logo=http://logo.example/two.png Live Two
            http://stream.example/live/two.m3u8?token=abc
            #EXTINF:-1 group-title="Movies",Trailer
            http://stream.example/movie/user/pass/900.mp4
            #EXTINF:-1 tvg-type="radio" group-title="Radio",Rock Radio
            http://stream.example/radio/rock.mp3
            """);
        var sourceId = await SeedSourceAsync(db, SourceType.M3U, playlistPath);

        await CreateM3uParser().ParseAndImportM3uAsync(db, sourceId, refreshHealth: false);

        var credential = await db.SourceCredentials.SingleAsync(item => item.SourceProfileId == sourceId);
        var channels = await db.ChannelCategories
            .Where(category => category.SourceProfileId == sourceId)
            .Join(db.Channels, category => category.Id, channel => channel.ChannelCategoryId, (category, channel) => new
            {
                CategoryName = category.Name,
                ChannelName = channel.Name,
                channel.StreamUrl,
                channel.LogoUrl,
                channel.ProviderEpgChannelId
            })
            .OrderBy(row => row.ChannelName)
            .ThenBy(row => row.ProviderEpgChannelId)
            .ToListAsync();

        Assert.AreEqual("https://guide.example/a.xml", credential.DetectedEpgUrl);
        Assert.AreEqual(2, channels.Count);
        Assert.IsTrue(channels.Any(channel => channel.ChannelName == "Live One" && channel.CategoryName == "News" && channel.LogoUrl == "https://img.example/logo.png"));
        Assert.IsTrue(channels.Any(channel => channel.ChannelName == "Live Two" && channel.ProviderEpgChannelId == "live.two"));
        Assert.AreEqual(0, await db.Movies.CountAsync(movie => movie.SourceProfileId == sourceId));
        Assert.AreEqual(0, await db.Series.CountAsync(series => series.SourceProfileId == sourceId));
        StringAssert.Contains((await db.SourceSyncStates.SingleAsync(item => item.SourceProfileId == sourceId)).ErrorLog, "duplicate_rejected=1");
        StringAssert.Contains((await db.SourceSyncStates.SingleAsync(item => item.SourceProfileId == sourceId)).ErrorLog, "radio_ignored=1");
    }

    [TestMethod]
    public async Task M3uImport_MixedLiveMovieAndSeriesClassifiesConservatively()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var playlistPath = await WritePlaylistAsync("""
            #EXTM3U epg-url=https://guide.example/guide.xml
            #EXTINF:-1 tvg-id="news" group-title="Live",News Channel
            http://stream.example/live/news.ts
            #EXTINF:-1 group-title="Movies",Example Movie
            http://stream.example/movie/user/pass/100.mp4
            #EXTINF:-1 group-title="Series",Example Show S01E01 Pilot
            http://stream.example/series/user/pass/201.mp4
            #EXTINF:-1 group-title="Series",Example Show S01E02 Next
            http://stream.example/series/user/pass/202.mp4
            #EXTINF:-1 group-title="Series",Ambiguous Standalone
            http://stream.example/series/user/pass/203.mp4
            """);
        var sourceId = await SeedSourceAsync(db, SourceType.M3U, playlistPath);

        await CreateM3uParser().ParseAndImportM3uAsync(db, sourceId, refreshHealth: false);

        Assert.AreEqual(1, await db.ChannelCategories.Where(category => category.SourceProfileId == sourceId).Join(db.Channels, category => category.Id, channel => channel.ChannelCategoryId, (category, channel) => channel.Id).CountAsync());
        Assert.AreEqual(2, await db.Movies.CountAsync(movie => movie.SourceProfileId == sourceId));
        var series = await db.Series.Include(item => item.Seasons!).ThenInclude(season => season.Episodes!).SingleAsync(item => item.SourceProfileId == sourceId);
        Assert.AreEqual("Example Show", series.Title);
        Assert.AreEqual(2, series.Seasons!.Single().Episodes!.Count);
    }

    [TestMethod]
    public async Task M3uImport_EmptySourceCompletesWithEmptyCatalog()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var playlistPath = await WritePlaylistAsync("""
            #EXTM3U
            # Empty provider file
            """);
        var sourceId = await SeedSourceAsync(db, SourceType.M3U, playlistPath);

        await CreateM3uParser().ParseAndImportM3uAsync(db, sourceId, refreshHealth: false);

        Assert.AreEqual(0, await db.ChannelCategories.CountAsync(category => category.SourceProfileId == sourceId));
        Assert.AreEqual(0, await db.Movies.CountAsync(movie => movie.SourceProfileId == sourceId));
        Assert.AreEqual(0, await db.Series.CountAsync(series => series.SourceProfileId == sourceId));
        StringAssert.Contains((await db.SourceSyncStates.SingleAsync(item => item.SourceProfileId == sourceId)).ErrorLog, "Parsed 0 channels");
    }

    [TestMethod]
    public async Task XtreamImport_ImportsLiveVodSeriesAndXmltvEndpoint()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, SourceType.Xtream, "https://xtream.example", "alice", "secret");
        var parser = CreateXtreamParser(new FixtureRoutingService(RespondXtreamSuccess));

        await parser.ParseAndImportXtreamAsync(db, sourceId, refreshHealth: false);
        await parser.ParseAndImportXtreamVodAsync(db, sourceId, refreshHealth: false);

        var credential = await db.SourceCredentials.SingleAsync(item => item.SourceProfileId == sourceId);
        Assert.AreEqual("https://xtream.example/xmltv.php?username=alice&password=secret", credential.DetectedEpgUrl);
        Assert.AreEqual(1, await db.ChannelCategories.Where(category => category.SourceProfileId == sourceId).Join(db.Channels, category => category.Id, channel => channel.ChannelCategoryId, (category, channel) => channel.Id).CountAsync());
        Assert.AreEqual(1, await db.Movies.CountAsync(movie => movie.SourceProfileId == sourceId));
        var series = await db.Series.Include(item => item.Seasons!).ThenInclude(season => season.Episodes!).SingleAsync(item => item.SourceProfileId == sourceId);
        Assert.AreEqual("Show One", series.Title);
        Assert.AreEqual(2, series.Seasons!.Single().Episodes!.Count);
    }

    [TestMethod]
    public async Task XtreamImport_MalformedResponseFailsWithSanitizedError()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, SourceType.Xtream, "https://xtream.example", "alice", "secret");
        var parser = CreateXtreamParser(new FixtureRoutingService(request =>
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            return query.Contains("action=get_live_categories", StringComparison.OrdinalIgnoreCase)
                ? Json(HttpStatusCode.OK, "{not json")
                : Json(HttpStatusCode.OK, """{"user_info":{"auth":1}}""");
        }));

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            parser.ParseAndImportXtreamAsync(db, sourceId, refreshHealth: false));

        StringAssert.Contains(ex.Message, "malformed JSON");
        var syncState = await db.SourceSyncStates.SingleAsync(item => item.SourceProfileId == sourceId);
        StringAssert.Contains(syncState.ErrorLog, "malformed JSON");
        Assert.IsFalse(syncState.ErrorLog.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(syncState.ErrorLog.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task XtreamImport_AuthFailureFailsWithSanitizedUnauthorizedStatus()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, SourceType.Xtream, "https://xtream.example", "alice", "secret");
        var parser = CreateXtreamParser(new FixtureRoutingService(_ => Json(HttpStatusCode.OK, """{"user_info":{"auth":0,"status":"Disabled"}}""")));

        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
            parser.ParseAndImportXtreamAsync(db, sourceId, refreshHealth: false));

        var syncState = await db.SourceSyncStates.SingleAsync(item => item.SourceProfileId == sourceId);
        Assert.AreEqual((int)HttpStatusCode.Unauthorized, syncState.HttpStatusCode);
        Assert.IsFalse(syncState.ErrorLog.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(syncState.ErrorLog.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private static HttpResponseMessage RespondXtreamSuccess(HttpRequestMessage request)
    {
        var query = request.RequestUri?.Query ?? string.Empty;
        if (!query.Contains("action=", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.OK, """{"user_info":{"auth":1,"status":"Active"}}""");
        }

        if (query.Contains("get_live_categories", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.OK, """[{"category_id":"1","category_name":"News"}]""");
        }

        if (query.Contains("get_live_streams", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.OK, """[{"stream_id":"11","category_id":"1","name":"News Channel","stream_icon":"https://img.example/news.png","epg_channel_id":"news"}]""");
        }

        if (query.Contains("get_vod_categories", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.OK, """[{"category_id":"2","category_name":"Movies"}]""");
        }

        if (query.Contains("get_vod_streams", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.OK, """[{"stream_id":"22","category_id":"2","name":"Movie One","stream_icon":"https://img.example/movie.png","container_extension":"mp4","tmdb":"123"}]""");
        }

        if (query.Contains("get_series_categories", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.OK, """[{"category_id":"3","category_name":"Series"}]""");
        }

        if (query.Contains("get_series_info", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.OK, """{"episodes":{"1":[{"id":"331","title":"Pilot","episode_num":1,"container_extension":"mp4"},{"id":"332","title":"Next","episode_num":2,"container_extension":"mp4"}]}}""");
        }

        if (query.Contains("get_series", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.OK, """[{"series_id":"33","category_id":"3","name":"Show One","cover":"https://img.example/show.png"}]""");
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static IM3uParserService CreateM3uParser()
    {
        return new M3uParserService(
            new CatalogNormalizationService(),
            new ChannelCatchupService(),
            new SourceEnrichmentService(new LiveChannelIdentityService()),
            new NoopSourceHealthService(),
            new SourceRoutingService());
    }

    private static IXtreamParserService CreateXtreamParser(ISourceRoutingService routingService)
    {
        return new XtreamParserService(
            new CatalogNormalizationService(),
            new ChannelCatchupService(),
            new SourceEnrichmentService(new LiveChannelIdentityService()),
            new NoopSourceHealthService(),
            routingService);
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
        SourceType sourceType,
        string url,
        string username = "",
        string password = "")
    {
        var profile = new SourceProfile
        {
            Name = "Fixture Source",
            Type = sourceType
        };
        db.SourceProfiles.Add(profile);
        await db.SaveChangesAsync();

        db.SourceCredentials.Add(new SourceCredential
        {
            SourceProfileId = profile.Id,
            Url = url,
            Username = username,
            Password = password,
            EpgMode = EpgActiveMode.Detected,
            M3uImportMode = M3uImportMode.LiveMoviesAndSeries
        });
        db.SourceSyncStates.Add(new SourceSyncState
        {
            SourceProfileId = profile.Id,
            LastAttempt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private static async Task<string> WritePlaylistAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kroira-{Guid.NewGuid():N}.m3u");
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private sealed class FixtureRoutingService : ISourceRoutingService
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FixtureRoutingService(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public SourceRoutingDecision Resolve(SourceCredential? credential, SourceNetworkPurpose purpose)
        {
            return new SourceRoutingDecision
            {
                Scope = SourceProxyScope.Disabled,
                Summary = "Fixture routing"
            };
        }

        public HttpClient CreateHttpClient(SourceCredential? credential, SourceNetworkPurpose purpose, TimeSpan timeout)
        {
            return new HttpClient(new FixtureHandler(_respond))
            {
                Timeout = timeout
            };
        }

        public void ApplyToPlaybackContext(SourceCredential? credential, PlaybackLaunchContext context)
        {
        }
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request));
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

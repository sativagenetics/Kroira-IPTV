using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class MediaSearchServiceTests
{
    [TestMethod]
    public async Task EmptyQuery_ReturnsSafeEmptyResult()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);

        var result = await new MediaSearchService().SearchAsync(db, "   ");

        Assert.IsTrue(result.IsEmptyQuery);
        Assert.AreEqual(0, result.TotalCount);
        Assert.AreEqual(4, result.Groups.Count);
    }

    [TestMethod]
    public async Task NormalizedQuery_MatchesCollapsedCaseInsensitiveTitle()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Fixture Source");
        await SeedChannelAsync(db, sourceId, "News", "World News", "https://stream.example/world-news.m3u8");

        var result = await new MediaSearchService().SearchAsync(db, "  WORLD    news  ");
        var live = GetGroup(result, MediaSearchResultType.Live);

        Assert.AreEqual(1, live.Results.Count);
        Assert.AreEqual("World News", live.Results[0].Title);
    }

    [TestMethod]
    public async Task GroupedResults_ReturnLiveMovieSeriesAndEpisodeSections()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Alpha Provider");
        await SeedChannelAsync(db, sourceId, "Live", "Alpha Live", "https://stream.example/alpha-live.m3u8");
        await SeedMovieAsync(db, sourceId, "Alpha Movie", "Action");
        var seriesId = await SeedSeriesAsync(db, sourceId, "Alpha Series", "Drama");
        await SeedEpisodeAsync(db, seriesId, 1, 1, "Alpha Pilot");

        var result = await new MediaSearchService().SearchAsync(db, "alpha");

        Assert.AreEqual(1, GetGroup(result, MediaSearchResultType.Live).Results.Count);
        Assert.AreEqual(1, GetGroup(result, MediaSearchResultType.Movie).Results.Count);
        Assert.AreEqual(1, GetGroup(result, MediaSearchResultType.Series).Results.Count);
        Assert.AreEqual(1, GetGroup(result, MediaSearchResultType.Episode).Results.Count);
    }

    [TestMethod]
    public async Task RelevanceOrdering_PrefersExactTitleOverContains()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Fixture Source");
        await SeedMovieAsync(db, sourceId, "News Extra", "Documentary");
        await SeedMovieAsync(db, sourceId, "News", "Documentary");

        var result = await new MediaSearchService().SearchAsync(db, "news");
        var movies = GetGroup(result, MediaSearchResultType.Movie);

        Assert.AreEqual(2, movies.Results.Count);
        Assert.AreEqual("News", movies.Results[0].Title);
        Assert.IsTrue(movies.Results[0].RelevanceScore > movies.Results[1].RelevanceScore);
    }

    [TestMethod]
    public async Task Cancellation_ThrowsWhenTokenAlreadyCanceled()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            new MediaSearchService().SearchAsync(db, "news", null, 12, cts.Token));
    }

    [TestMethod]
    public async Task SourceAndCategoryBadges_MapFromCatalogRows()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db, "Provider A");
        await SeedChannelAsync(db, sourceId, "Sports", "Match One", "https://stream.example/match-one.m3u8");

        var result = await new MediaSearchService().SearchAsync(db, "match");
        var live = GetGroup(result, MediaSearchResultType.Live).Results.Single();

        Assert.AreEqual("Provider A", live.SourceBadge);
        Assert.AreEqual("Sports", live.CategoryBadge);
    }

    private static MediaSearchResultGroup GetGroup(MediaSearchResponse response, MediaSearchResultType type)
    {
        return response.Groups.Single(group => group.Type == type);
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

    private static async Task<int> SeedSourceAsync(AppDbContext db, string name)
    {
        var source = new SourceProfile
        {
            Name = name,
            Type = SourceType.M3U,
            LastSync = DateTime.UtcNow
        };
        db.SourceProfiles.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private static async Task<int> SeedChannelAsync(
        AppDbContext db,
        int sourceId,
        string categoryName,
        string channelName,
        string streamUrl)
    {
        var category = new ChannelCategory
        {
            SourceProfileId = sourceId,
            Name = categoryName,
            OrderIndex = 0
        };
        db.ChannelCategories.Add(category);
        await db.SaveChangesAsync();

        var channel = new Channel
        {
            ChannelCategoryId = category.Id,
            Name = channelName,
            StreamUrl = streamUrl,
            LogoUrl = "https://img.example/channel.png",
            EpgChannelId = channelName.ToLowerInvariant().Replace(' ', '.'),
            ProviderEpgChannelId = channelName.ToLowerInvariant().Replace(' ', '.')
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

    private static async Task<int> SeedMovieAsync(AppDbContext db, int sourceId, string title, string categoryName)
    {
        var movie = new Movie
        {
            SourceProfileId = sourceId,
            Title = title,
            StreamUrl = $"https://stream.example/movie/{Uri.EscapeDataString(title)}.mp4",
            CategoryName = categoryName,
            RawSourceCategoryName = categoryName,
            PosterUrl = "https://img.example/movie.jpg",
            ContentKind = "Primary"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    private static async Task<int> SeedSeriesAsync(AppDbContext db, int sourceId, string title, string categoryName)
    {
        var series = new Series
        {
            SourceProfileId = sourceId,
            Title = title,
            CategoryName = categoryName,
            RawSourceCategoryName = categoryName,
            PosterUrl = "https://img.example/series.jpg",
            ContentKind = "Primary"
        };
        db.Series.Add(series);
        await db.SaveChangesAsync();
        return series.Id;
    }

    private static async Task<int> SeedEpisodeAsync(
        AppDbContext db,
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        string title)
    {
        var season = new Season
        {
            SeriesId = seriesId,
            SeasonNumber = seasonNumber
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();

        var episode = new Episode
        {
            SeasonId = season.Id,
            EpisodeNumber = episodeNumber,
            Title = title,
            StreamUrl = $"https://stream.example/episode/{Uri.EscapeDataString(title)}.mp4"
        };
        db.Episodes.Add(episode);
        await db.SaveChangesAsync();
        return episode.Id;
    }
}

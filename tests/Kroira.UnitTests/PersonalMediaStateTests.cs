using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class PersonalMediaStateTests
{
    [TestMethod]
    public async Task FavoriteDeduplication_ReconcileKeepsOneLogicalFavorite()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var logicalState = CreateLogicalStateService();
        var sourceId = await SeedSourceAsync(db);
        var channel = await SeedChannelAsync(db, sourceId, "News", "World News");
        var logicalKey = logicalState.BuildChannelLogicalKey(channel.Channel);

        db.Favorites.AddRange(
            new Favorite
            {
                ProfileId = 1,
                ContentType = FavoriteType.Channel,
                ContentId = channel.Channel.Id,
                LogicalContentKey = logicalKey,
                PreferredSourceProfileId = sourceId,
                ResolvedAtUtc = DateTime.UtcNow.AddMinutes(-5)
            },
            new Favorite
            {
                ProfileId = 1,
                ContentType = FavoriteType.Channel,
                ContentId = channel.Channel.Id,
                LogicalContentKey = logicalKey,
                PreferredSourceProfileId = sourceId,
                ResolvedAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        await logicalState.ReconcileFavoritesAsync(db, 1);

        var favorites = await db.Favorites.AsNoTracking().ToListAsync();
        Assert.AreEqual(1, favorites.Count);
        Assert.AreEqual(logicalKey, favorites[0].LogicalContentKey);
    }

    [TestMethod]
    public async Task ProgressUpdate_StoresAccurateResumeSnapshot()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db);
        var movie = await SeedMovieAsync(db, sourceId, "Resume Movie");
        var logicalState = CreateLogicalStateService();
        var watchState = new LibraryWatchStateService();
        var logicalKey = logicalState.BuildMovieLogicalKey(movie);
        var watchedAtUtc = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        await watchState.UpsertProgressAsync(
            db,
            profileId: 1,
            contentType: PlaybackContentType.Movie,
            contentId: movie.Id,
            positionMs: 60_000,
            durationMs: 600_000,
            logicalContentKey: logicalKey,
            preferredSourceProfileId: sourceId,
            watchedAtUtc: watchedAtUtc);

        var snapshot = (await watchState.LoadSnapshotsAsync(db, 1, PlaybackContentType.Movie, new[] { movie.Id }))[movie.Id];
        Assert.AreEqual(60_000, snapshot.ResumePositionMs);
        Assert.AreEqual(600_000, snapshot.DurationMs);
        Assert.AreEqual(10d, snapshot.ProgressPercent, 0.001);
        Assert.IsTrue(snapshot.HasResumePoint);
        Assert.IsFalse(snapshot.IsWatched);
        Assert.AreEqual(logicalKey, snapshot.LogicalContentKey);
        Assert.AreEqual(sourceId, snapshot.PreferredSourceProfileId);
        Assert.AreEqual(watchedAtUtc, snapshot.LastWatched);
    }

    [TestMethod]
    public async Task CompletedThreshold_MarksNearEndProgressAsWatched()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db);
        var movie = await SeedMovieAsync(db, sourceId, "Almost Finished");
        var watchState = new LibraryWatchStateService();

        await watchState.UpsertProgressAsync(
            db,
            profileId: 1,
            contentType: PlaybackContentType.Movie,
            contentId: movie.Id,
            positionMs: 490_000,
            durationMs: 600_000);

        var snapshot = (await watchState.LoadSnapshotsAsync(db, 1, PlaybackContentType.Movie, new[] { movie.Id }))[movie.Id];
        Assert.IsTrue(snapshot.IsCompleted);
        Assert.IsTrue(snapshot.IsWatched);
        Assert.IsFalse(snapshot.HasResumePoint);
        Assert.AreEqual(0, snapshot.ResumePositionMs);
        Assert.AreEqual(100d, snapshot.ProgressPercent, 0.001);
    }

    [TestMethod]
    public async Task SourceDeletionFallback_RemovesOrphanedFavoriteAndProgress()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var sourceId = await SeedSourceAsync(db);
        var movie = await SeedMovieAsync(db, sourceId, "Deleted Source Movie");
        var logicalState = CreateLogicalStateService();
        var logicalKey = logicalState.BuildMovieLogicalKey(movie);

        db.Favorites.Add(new Favorite
        {
            ProfileId = 1,
            ContentType = FavoriteType.Movie,
            ContentId = movie.Id,
            LogicalContentKey = logicalKey,
            PreferredSourceProfileId = sourceId,
            ResolvedAtUtc = DateTime.UtcNow
        });
        db.PlaybackProgresses.Add(new PlaybackProgress
        {
            ProfileId = 1,
            ContentType = PlaybackContentType.Movie,
            ContentId = movie.Id,
            LogicalContentKey = logicalKey,
            PreferredSourceProfileId = sourceId,
            PositionMs = 45_000,
            DurationMs = 600_000,
            LastWatched = DateTime.UtcNow,
            ResolvedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var source = await db.SourceProfiles.SingleAsync(item => item.Id == sourceId);
        db.SourceProfiles.Remove(source);
        await db.SaveChangesAsync();

        await logicalState.ReconcileFavoritesAsync(db, 1);
        await logicalState.ReconcilePlaybackProgressAsync(db, 1);

        Assert.AreEqual(0, await db.Favorites.CountAsync());
        Assert.AreEqual(0, await db.PlaybackProgresses.CountAsync());
    }

    [TestMethod]
    public async Task LockedCategoryFiltering_HidesLockedLiveCategoryUntilUnlocked()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var profileState = new ProfileStateService();
        var profile = await profileState.GetActiveProfileAsync(db);
        var sourceId = await SeedSourceAsync(db);
        var channel = await SeedChannelAsync(db, sourceId, "Locked Sports", "Match Channel");

        await profileState.SetLockedCategoryKeysAsync(
            db,
            profile.Id,
            new[] { ProfileStateService.MakeCategoryLockKey(ProfileDomains.Live, "Locked Sports") });

        var access = await profileState.GetAccessSnapshotAsync(db);
        Assert.IsFalse(access.IsLiveChannelAllowed(channel.Channel, channel.Category));

        await profileState.SetHideLockedContentAsync(db, profile.Id, false);
        access = await profileState.GetAccessSnapshotAsync(db);
        Assert.IsTrue(access.IsLiveChannelAllowed(channel.Channel, channel.Category));
    }

    [TestMethod]
    public async Task ProfileDefaultBehavior_KeepsFavoritesAndProgressScoped()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var profileState = new ProfileStateService();
        var primary = await profileState.GetActiveProfileAsync(db);
        var secondary = await profileState.CreateProfileAsync(db, "Guest", isKidsProfile: false);
        var sourceId = await SeedSourceAsync(db);
        var movie = await SeedMovieAsync(db, sourceId, "Scoped Movie");
        var logicalState = CreateLogicalStateService();
        var logicalKey = logicalState.BuildMovieLogicalKey(movie);

        db.Favorites.Add(new Favorite
        {
            ProfileId = primary.Id,
            ContentType = FavoriteType.Movie,
            ContentId = movie.Id,
            LogicalContentKey = logicalKey,
            PreferredSourceProfileId = sourceId
        });
        db.PlaybackProgresses.Add(new PlaybackProgress
        {
            ProfileId = primary.Id,
            ContentType = PlaybackContentType.Movie,
            ContentId = movie.Id,
            LogicalContentKey = logicalKey,
            PreferredSourceProfileId = sourceId,
            PositionMs = 30_000,
            DurationMs = 600_000,
            LastWatched = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await profileState.SwitchProfileAsync(db, secondary.Id);
        var access = await profileState.GetAccessSnapshotAsync(db);

        Assert.AreEqual(secondary.Id, access.ProfileId);
        Assert.AreEqual("Guest", access.ProfileName);
        Assert.AreEqual(0, await db.Favorites.CountAsync(item => item.ProfileId == access.ProfileId));
        Assert.AreEqual(0, await db.PlaybackProgresses.CountAsync(item => item.ProfileId == access.ProfileId));
        Assert.AreEqual(1, await db.Favorites.CountAsync(item => item.ProfileId == primary.Id));
        Assert.AreEqual(1, await db.PlaybackProgresses.CountAsync(item => item.ProfileId == primary.Id));
    }

    private static LogicalCatalogStateService CreateLogicalStateService()
    {
        return new LogicalCatalogStateService(
            new LiveChannelIdentityService(),
            new BrowsePreferencesService());
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

    private static async Task<int> SeedSourceAsync(AppDbContext db)
    {
        var source = new SourceProfile
        {
            Name = "Personal Source",
            Type = SourceType.M3U,
            LastSync = DateTime.UtcNow
        };
        db.SourceProfiles.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private static async Task<ChannelSeedResult> SeedChannelAsync(
        AppDbContext db,
        int sourceId,
        string categoryName,
        string channelName)
    {
        var category = new ChannelCategory
        {
            SourceProfileId = sourceId,
            Name = categoryName,
            OrderIndex = 0
        };
        db.ChannelCategories.Add(category);
        await db.SaveChangesAsync();

        var identity = new LiveChannelIdentityService().Build(channelName, channelName.ToLowerInvariant().Replace(' ', '.'));
        var channel = new Channel
        {
            ChannelCategoryId = category.Id,
            Name = channelName,
            StreamUrl = $"https://stream.example/live/{Uri.EscapeDataString(channelName)}.m3u8",
            LogoUrl = "https://img.example/live.png",
            EpgChannelId = channelName.ToLowerInvariant().Replace(' ', '.'),
            ProviderEpgChannelId = channelName.ToLowerInvariant().Replace(' ', '.'),
            NormalizedIdentityKey = identity.IdentityKey,
            NormalizedName = identity.NormalizedName,
            AliasKeys = string.Join("\n", identity.AliasKeys)
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        return new ChannelSeedResult(category, channel);
    }

    private static async Task<Movie> SeedMovieAsync(AppDbContext db, int sourceId, string title)
    {
        var movie = new Movie
        {
            SourceProfileId = sourceId,
            Title = title,
            StreamUrl = $"https://stream.example/movie/{Uri.EscapeDataString(title)}.mp4",
            CategoryName = "Movies",
            RawSourceCategoryName = "Movies",
            ContentKind = "Primary"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie;
    }

    private sealed record ChannelSeedResult(ChannelCategory Category, Channel Channel);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Parsing
{
    public interface IXtreamParserService
    {
        Task ParseAndImportXtreamAsync(AppDbContext db, int sourceProfileId);
        Task ParseAndImportXtreamVodAsync(AppDbContext db, int sourceProfileId);
    }

    public class XtreamParserService : IXtreamParserService
    {
        public async Task ParseAndImportXtreamAsync(AppDbContext db, int sourceProfileId)
        {
            var profile = await db.SourceProfiles.FindAsync(sourceProfileId);
            if (profile == null) throw new Exception("Source not found.");

            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url) || string.IsNullOrWhiteSpace(cred.Username))
                throw new Exception("Xtream credentials are incomplete.");

            string baseUrl = cred.Url.TrimEnd('/');
            string authQuery = $"?username={Uri.EscapeDataString(cred.Username)}&password={Uri.EscapeDataString(cred.Password)}";
            string catsUrl = $"{baseUrl}/player_api.php{authQuery}&action=get_live_categories";
            string streamsUrl = $"{baseUrl}/player_api.php{authQuery}&action=get_live_streams";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            try
            {
                var catsResponse = await client.GetAsync(catsUrl);
                catsResponse.EnsureSuccessStatusCode();
                var catsJson = await catsResponse.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(catsJson)) catsJson = "[]";
                using var catsDoc = JsonDocument.Parse(catsJson);

                var streamsResponse = await client.GetAsync(streamsUrl);
                streamsResponse.EnsureSuccessStatusCode();
                var streamsJson = await streamsResponse.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(streamsJson)) streamsJson = "[]";
                using var streamsDoc = JsonDocument.Parse(streamsJson);

                using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    var oldCats = await db.ChannelCategories.Where(c => c.SourceProfileId == sourceProfileId).ToListAsync();
                    var oldCatIds = oldCats.Select(c => c.Id).ToList();
                    var oldChans = await db.Channels.Where(c => oldCatIds.Contains(c.ChannelCategoryId)).ToListAsync();

                    var oldFavs = await db.Favorites.Where(f => f.ContentType == FavoriteType.Channel && oldChans.Select(c => c.Id).Contains(f.ContentId)).ToListAsync();
                    db.Favorites.RemoveRange(oldFavs);

                    var oldEpg = await db.EpgPrograms.Where(e => oldChans.Select(c => c.Id).Contains(e.ChannelId)).ToListAsync();
                    db.EpgPrograms.RemoveRange(oldEpg);

                    db.Channels.RemoveRange(oldChans);
                    db.ChannelCategories.RemoveRange(oldCats);
                    await db.SaveChangesAsync();

                    var categoryMap = new Dictionary<string, ChannelCategory>();

                    if (catsDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in catsDoc.RootElement.EnumerateArray())
                        {
                            string id = null;
                            if (element.TryGetProperty("category_id", out var idProp))
                            {
                                id = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString();
                            }

                            var name = element.TryGetProperty("category_name", out var nameProp) ? nameProp.GetString() : "Unknown";

                            if (!string.IsNullOrEmpty(id))
                            {
                                var cat = new ChannelCategory
                                {
                                    SourceProfileId = sourceProfileId,
                                    Name = name ?? "Unknown",
                                    OrderIndex = categoryMap.Count
                                };
                                db.ChannelCategories.Add(cat);
                                categoryMap[id] = cat;
                            }
                        }
                        await db.SaveChangesAsync();
                    }

                    if (streamsDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var channelsList = new List<Channel>();
                        var liveCategoryLabels = ContentClassifier.BuildCategoryLabelSet(categoryMap.Values.Select(c => c.Name));
                        foreach (var element in streamsDoc.RootElement.EnumerateArray())
                        {
                            string catId = null;
                            if (element.TryGetProperty("category_id", out var cIdProp))
                            {
                                catId = cIdProp.ValueKind == JsonValueKind.Number ? cIdProp.GetInt32().ToString() : cIdProp.GetString();
                            }

                            string streamId = null;
                            if (element.TryGetProperty("stream_id", out var sProp))
                            {
                                streamId = sProp.ValueKind == JsonValueKind.Number ? sProp.GetInt32().ToString() : sProp.GetString();
                            }

                            var name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() : "Unknown";
                            var logo = element.TryGetProperty("stream_icon", out var lProp) ? lProp.GetString() : string.Empty;

                            if (string.IsNullOrEmpty(streamId)) continue;

                            if (catId != null && categoryMap.TryGetValue(catId, out var mappedCat))
                            {
                                string streamUrl = $"{baseUrl}/live/{cred.Username}/{cred.Password}/{streamId}.ts";
                                if (!ContentClassifier.IsPlayableLiveChannel(name ?? string.Empty, streamUrl, liveCategoryLabels)) continue;

                                channelsList.Add(new Channel
                                {
                                    ChannelCategoryId = mappedCat.Id,
                                    Name = string.IsNullOrWhiteSpace(name) ? "Unknown Channel" : name,
                                    StreamUrl = streamUrl,
                                    LogoUrl = logo ?? string.Empty
                                });
                            }
                        }

                        db.Channels.AddRange(channelsList);
                        await db.SaveChangesAsync();
                    }

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                    if (syncState != null)
                    {
                        syncState.LastAttempt = DateTime.UtcNow;
                        syncState.HttpStatusCode = 200;
                        syncState.ErrorLog = "Xtream Live Sync: Imported successfully.";
                    }

                    profile.LastSync = DateTime.UtcNow;

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = 500;
                    syncState.ErrorLog = $"Xtream Live sync failed: {ex.Message}";
                    await db.SaveChangesAsync();
                }
                throw;
            }
        }

        public async Task ParseAndImportXtreamVodAsync(AppDbContext db, int sourceProfileId)
        {
            var profile = await db.SourceProfiles.FindAsync(sourceProfileId);
            if (profile == null) throw new Exception("Source not found.");

            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url) || string.IsNullOrWhiteSpace(cred.Username))
                throw new Exception("Xtream credentials are incomplete.");

            string baseUrl = cred.Url.TrimEnd('/');
            string authQuery = $"?username={Uri.EscapeDataString(cred.Username)}&password={Uri.EscapeDataString(cred.Password)}";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

            try
            {
                var movCatsJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_vod_categories");
                if (string.IsNullOrWhiteSpace(movCatsJson)) movCatsJson = "[]";
                using var movCatsDoc = JsonDocument.Parse(movCatsJson);
                var md = new Dictionary<string, string>();
                if (movCatsDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in movCatsDoc.RootElement.EnumerateArray())
                    {
                        string id = element.TryGetProperty("category_id", out var idProp) ? (idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString()) : null;
                        if (!string.IsNullOrEmpty(id) && element.TryGetProperty("category_name", out var nameProp))
                        {
                            md[id] = nameProp.GetString() ?? "Unknown";
                        }
                    }
                }
                var movieCategoryLabels = ContentClassifier.BuildCategoryLabelSet(md.Values);

                var moviesJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_vod_streams");
                if (string.IsNullOrWhiteSpace(moviesJson)) moviesJson = "[]";
                using var moviesDoc = JsonDocument.Parse(moviesJson);
                var parsedMovies = new List<Movie>();

                if (moviesDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in moviesDoc.RootElement.EnumerateArray())
                    {
                        string streamId = element.TryGetProperty("stream_id", out var sProp) ? (sProp.ValueKind == JsonValueKind.Number ? sProp.GetInt32().ToString() : sProp.GetString()) : null;
                        string catId = element.TryGetProperty("category_id", out var cProp) ? (cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt32().ToString() : cProp.GetString()) : null;

                        if (string.IsNullOrEmpty(streamId)) continue;

                        var ext = (element.TryGetProperty("container_extension", out var exProp) ? exProp.GetString() : null) ?? "mp4";
                        var name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() : "Unknown";
                        var logo = element.TryGetProperty("stream_icon", out var lProp) ? lProp.GetString() : string.Empty;

                        md.TryGetValue(catId ?? "", out var mappedCatName);

                        // --- Garbage filter ---
                        if (ContentClassifier.IsGarbageMovieExtension(ext)) continue;
                        if (ContentClassifier.IsGarbageCategoryName(mappedCatName)) continue;
                        if (!ContentClassifier.IsPlayableMovie(new Movie
                        {
                            Title = name ?? string.Empty,
                            StreamUrl = $"{baseUrl}/movie/{cred.Username}/{cred.Password}/{streamId}.{ext}"
                        }, movieCategoryLabels)) continue;
                        // ----------------------

                        parsedMovies.Add(new Movie
                        {
                            SourceProfileId = sourceProfileId,
                            ExternalId = streamId,
                            Title = string.IsNullOrWhiteSpace(name) ? "Unknown Movie" : name,
                            StreamUrl = $"{baseUrl}/movie/{cred.Username}/{cred.Password}/{streamId}.{ext}",
                            PosterUrl = logo ?? string.Empty,
                            CategoryName = mappedCatName ?? "Uncategorized"
                        });
                    }
                }

                var serCatsJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_series_categories");
                if (string.IsNullOrWhiteSpace(serCatsJson)) serCatsJson = "[]";
                using var serCatsDoc = JsonDocument.Parse(serCatsJson);
                var sd = new Dictionary<string, string>();
                if (serCatsDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in serCatsDoc.RootElement.EnumerateArray())
                    {
                        string id = element.TryGetProperty("category_id", out var idProp) ? (idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString()) : null;
                        if (!string.IsNullOrEmpty(id) && element.TryGetProperty("category_name", out var nameProp))
                        {
                            sd[id] = nameProp.GetString() ?? "Unknown";
                        }
                    }
                }
                var seriesCategoryLabels = ContentClassifier.BuildCategoryLabelSet(sd.Values);

                var seriesJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_series");
                if (string.IsNullOrWhiteSpace(seriesJson)) seriesJson = "[]";
                using var seriesDoc = JsonDocument.Parse(seriesJson);
                var pendingSeries = new List<(string SeriesId, Series BaseObj)>();

                if (seriesDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in seriesDoc.RootElement.EnumerateArray())
                    {
                        string seriesId = element.TryGetProperty("series_id", out var sProp) ? (sProp.ValueKind == JsonValueKind.Number ? sProp.GetInt32().ToString() : sProp.GetString()) : null;
                        string catId = element.TryGetProperty("category_id", out var cProp) ? (cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt32().ToString() : cProp.GetString()) : null;

                        if (string.IsNullOrEmpty(seriesId)) continue;

                        var name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() : "Unknown";
                        var cover = element.TryGetProperty("cover", out var lProp) ? lProp.GetString() : string.Empty;

                        sd.TryGetValue(catId ?? "", out var mappedCatName);

                        // --- Garbage filter ---
                        if (ContentClassifier.IsGarbageCategoryName(mappedCatName)) continue;
                        if (ContentClassifier.IsGarbageTitle(name ?? string.Empty)) continue;
                        if (ContentClassifier.IsProviderCategoryRow(name ?? string.Empty, seriesCategoryLabels)) continue;
                        // ----------------------

                        pendingSeries.Add((seriesId, new Series
                        {
                            SourceProfileId = sourceProfileId,
                            ExternalId = seriesId,
                            Title = string.IsNullOrWhiteSpace(name) ? "Unknown Series" : name,
                            PosterUrl = cover ?? string.Empty,
                            CategoryName = mappedCatName ?? "Uncategorized",
                            Seasons = new List<Season>()
                        }));
                    }
                }

                var limitedSeries = pendingSeries.Take(30).ToList();
                if (limitedSeries.Any())
                {
                    var semaphore = new System.Threading.SemaphoreSlim(8);
                    var seriesTasks = limitedSeries.Select(async sInfo =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var infoStr = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_series_info&series_id={sInfo.SeriesId}");
                            if (string.IsNullOrWhiteSpace(infoStr)) return;
                            using var iDoc = JsonDocument.Parse(infoStr);

                            if (iDoc.RootElement.TryGetProperty("episodes", out var epNode) && epNode.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var seasonProp in epNode.EnumerateObject())
                                {
                                    if (!int.TryParse(seasonProp.Name, out var seasonNum)) continue;

                                    var seasonObj = new Season
                                    {
                                        SeasonNumber = seasonNum,
                                        Episodes = new List<Episode>()
                                    };

                                    if (seasonProp.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var epElement in seasonProp.Value.EnumerateArray())
                                        {
                                            string epId = epElement.TryGetProperty("id", out var epProp) ? (epProp.ValueKind == JsonValueKind.Number ? epProp.GetInt32().ToString() : epProp.GetString()) : null;
                                            if (string.IsNullOrEmpty(epId)) continue;

                                            var epName = epElement.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                                            var ext = epElement.TryGetProperty("container_extension", out var exProp) ? exProp.GetString() : "mp4";
                                            var epNum = epElement.TryGetProperty("episode_num", out var enumProp) ? (enumProp.ValueKind == JsonValueKind.Number ? enumProp.GetInt32() : (int.TryParse(enumProp.GetString(), out var ev) ? ev : 0)) : 0;

                                            seasonObj.Episodes.Add(new Episode
                                            {
                                                ExternalId = epId,
                                                Title = string.IsNullOrWhiteSpace(epName) ? $"Episode {epNum}" : epName,
                                                EpisodeNumber = epNum,
                                                StreamUrl = $"{baseUrl}/series/{cred.Username}/{cred.Password}/{epId}.{ext}"
                                            });
                                        }
                                    }
                                    sInfo.BaseObj.Seasons.Add(seasonObj);
                                }
                            }
                        }
                        catch { } // Suppress gracefully per entity
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(seriesTasks);
                }

                using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    // ── MOVIES UPSERT ────────────────────────────────────────────────────────
                    // Load existing movies for this source keyed by ExternalId
                    var existingMovies = await db.Movies
                        .Where(m => m.SourceProfileId == sourceProfileId)
                        .ToListAsync();
                    var existingMovieMap = existingMovies
                        .Where(m => !string.IsNullOrEmpty(m.ExternalId))
                        .ToDictionary(m => m.ExternalId);

                    var incomingMovieIds = new HashSet<string>(parsedMovies.Select(p => p.ExternalId));

                    // Update or insert
                    int movInserted = 0, movUpdated = 0;
                    foreach (var incoming in parsedMovies)
                    {
                        if (existingMovieMap.TryGetValue(incoming.ExternalId, out var existing))
                        {
                            // UPDATE in place — Id stays the same, favorites/progress survive
                            existing.Title = incoming.Title;
                            existing.StreamUrl = incoming.StreamUrl;
                            existing.PosterUrl = incoming.PosterUrl;
                            existing.CategoryName = incoming.CategoryName;
                            movUpdated++;
                        }
                        else
                        {
                            db.Movies.Add(incoming);
                            movInserted++;
                        }
                    }

                    // Delete movies no longer in feed (also clean up their favorites/progress)
                    var staleMovies = existingMovies
                        .Where(m => !string.IsNullOrEmpty(m.ExternalId) && !incomingMovieIds.Contains(m.ExternalId))
                        .ToList();
                    if (staleMovies.Count > 0)
                    {
                        var staleIds = staleMovies.Select(m => m.Id).ToList();
                        var staleFavs = await db.Favorites
                            .Where(f => f.ContentType == FavoriteType.Movie && staleIds.Contains(f.ContentId))
                            .ToListAsync();
                        db.Favorites.RemoveRange(staleFavs);
                        var staleProgress = await db.PlaybackProgresses
                            .Where(p => p.ContentType == PlaybackContentType.Movie && staleIds.Contains(p.ContentId))
                            .ToListAsync();
                        db.PlaybackProgresses.RemoveRange(staleProgress);
                        db.Movies.RemoveRange(staleMovies);
                    }

                    // Orphaned movies (no ExternalId from before this migration) — delete safely
                    var orphanMovies = existingMovies
                        .Where(m => string.IsNullOrEmpty(m.ExternalId))
                        .ToList();
                    if (orphanMovies.Count > 0)
                        db.Movies.RemoveRange(orphanMovies);

                    await db.SaveChangesAsync();

                    // ── SERIES UPSERT ────────────────────────────────────────────────────────
                    var validSeriesInfos = limitedSeries
                        .Where(s => s.BaseObj.Seasons != null && s.BaseObj.Seasons.Any(sn => sn.Episodes != null && sn.Episodes.Count > 0))
                        .ToList();

                    var existingSeries = await db.Series
                        .Include(s => s.Seasons!).ThenInclude(sn => sn.Episodes!)
                        .Where(s => s.SourceProfileId == sourceProfileId)
                        .ToListAsync();
                    var existingSeriesMap = existingSeries
                        .Where(s => !string.IsNullOrEmpty(s.ExternalId))
                        .ToDictionary(s => s.ExternalId);

                    var incomingSeriesIds = new HashSet<string>(validSeriesInfos.Select(s => s.SeriesId));

                    int serInserted = 0, serUpdated = 0;
                    foreach (var sInfo in validSeriesInfos)
                    {
                        if (existingSeriesMap.TryGetValue(sInfo.SeriesId, out var existingSer))
                        {
                            // UPDATE metadata in place — Series.Id stays the same
                            existingSer.Title = sInfo.BaseObj.Title;
                            existingSer.PosterUrl = sInfo.BaseObj.PosterUrl;
                            existingSer.CategoryName = sInfo.BaseObj.CategoryName;

                            // Upsert seasons and episodes instead of rebuild-from-scratch
                            var existingSeasonMap = (existingSer.Seasons ?? Enumerable.Empty<Season>())
                                .ToDictionary(sn => sn.SeasonNumber);
                            var incomingSeasonNums = new HashSet<int>(sInfo.BaseObj.Seasons!.Select(sn => sn.SeasonNumber));

                            foreach (var incomingSeason in sInfo.BaseObj.Seasons!)
                            {
                                if (existingSeasonMap.TryGetValue(incomingSeason.SeasonNumber, out var existingSeason))
                                {
                                    // Season exists — upsert its episodes
                                    var existingEpMap = (existingSeason.Episodes ?? Enumerable.Empty<Episode>())
                                        .Where(e => !string.IsNullOrEmpty(e.ExternalId))
                                        .ToDictionary(e => e.ExternalId);
                                    var incomingEpExternalIds = new HashSet<string>(
                                        incomingSeason.Episodes!.Select(e => e.ExternalId)
                                            .Where(x => !string.IsNullOrEmpty(x)));

                                    foreach (var incomingEp in incomingSeason.Episodes!)
                                    {
                                        if (!string.IsNullOrEmpty(incomingEp.ExternalId) &&
                                            existingEpMap.TryGetValue(incomingEp.ExternalId, out var existingEp))
                                        {
                                            // UPDATE — Episode.Id unchanged, progress survives
                                            existingEp.Title = incomingEp.Title;
                                            existingEp.StreamUrl = incomingEp.StreamUrl;
                                            existingEp.EpisodeNumber = incomingEp.EpisodeNumber;
                                        }
                                        else
                                        {
                                            incomingEp.SeasonId = existingSeason.Id;
                                            db.Episodes.Add(incomingEp);
                                        }
                                    }

                                    // Remove episodes no longer in feed
                                    var staleEps = (existingSeason.Episodes ?? Enumerable.Empty<Episode>())
                                        .Where(e => !string.IsNullOrEmpty(e.ExternalId) &&
                                                    !incomingEpExternalIds.Contains(e.ExternalId))
                                        .ToList();
                                    if (staleEps.Count > 0)
                                    {
                                        var staleEpIds = staleEps.Select(e => e.Id).ToList();
                                        var staleEpProgress = await db.PlaybackProgresses
                                            .Where(p => p.ContentType == PlaybackContentType.Episode && staleEpIds.Contains(p.ContentId))
                                            .ToListAsync();
                                        db.PlaybackProgresses.RemoveRange(staleEpProgress);
                                        db.Episodes.RemoveRange(staleEps);
                                    }

                                    // Orphan episodes (no ExternalId, pre-migration)
                                    var orphanEps = (existingSeason.Episodes ?? Enumerable.Empty<Episode>())
                                        .Where(e => string.IsNullOrEmpty(e.ExternalId))
                                        .ToList();
                                    if (orphanEps.Count > 0) db.Episodes.RemoveRange(orphanEps);
                                }
                                else
                                {
                                    // New season — insert whole branch; set FK explicitly
                                    incomingSeason.SeriesId = existingSer.Id;
                                    db.Seasons.Add(incomingSeason);
                                }
                            }

                            // Remove seasons no longer in feed
                            var staleSns = (existingSer.Seasons ?? Enumerable.Empty<Season>())
                                .Where(sn => !incomingSeasonNums.Contains(sn.SeasonNumber))
                                .ToList();
                            if (staleSns.Count > 0)
                            {
                                foreach (var staleSn in staleSns)
                                    if (staleSn.Episodes != null) db.Episodes.RemoveRange(staleSn.Episodes);
                                db.Seasons.RemoveRange(staleSns);
                            }

                            serUpdated++;
                        }
                        else
                        {
                            db.Series.Add(sInfo.BaseObj);
                            serInserted++;
                        }
                    }

                    // Delete series no longer in feed
                    var staleSeries = existingSeries
                        .Where(s => !string.IsNullOrEmpty(s.ExternalId) && !incomingSeriesIds.Contains(s.ExternalId))
                        .ToList();
                    if (staleSeries.Count > 0)
                    {
                        var staleSerIds = staleSeries.Select(s => s.Id).ToList();
                        var staleSerFavs = await db.Favorites
                            .Where(f => f.ContentType == FavoriteType.Series && staleSerIds.Contains(f.ContentId))
                            .ToListAsync();
                        db.Favorites.RemoveRange(staleSerFavs);
                        foreach (var ser in staleSeries)
                        {
                            if (ser.Seasons != null)
                            {
                                foreach (var sn in ser.Seasons)
                                    if (sn.Episodes != null) db.Episodes.RemoveRange(sn.Episodes);
                                db.Seasons.RemoveRange(ser.Seasons);
                            }
                        }
                        db.Series.RemoveRange(staleSeries);
                    }

                    // Orphaned series (no ExternalId)
                    var orphanSeries = existingSeries
                        .Where(s => string.IsNullOrEmpty(s.ExternalId))
                        .ToList();
                    if (orphanSeries.Count > 0)
                    {
                        foreach (var ser in orphanSeries)
                        {
                            if (ser.Seasons != null)
                            {
                                foreach (var sn in ser.Seasons)
                                    if (sn.Episodes != null) db.Episodes.RemoveRange(sn.Episodes);
                                db.Seasons.RemoveRange(ser.Seasons);
                            }
                        }
                        db.Series.RemoveRange(orphanSeries);
                    }

                    await db.SaveChangesAsync();

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                    if (syncState != null)
                    {
                        syncState.LastAttempt = DateTime.UtcNow;
                        syncState.HttpStatusCode = 200;
                        syncState.ErrorLog = $"Xtream VOD Sync: movies +{movInserted}/~{movUpdated}, series +{serInserted}/~{serUpdated}.";
                    }

                    profile.LastSync = DateTime.UtcNow;

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = 500;
                    syncState.ErrorLog = $"Xtream VOD parsing failed: {ex.Message}";
                    await db.SaveChangesAsync();
                }
                throw;
            }
        }

    }
}

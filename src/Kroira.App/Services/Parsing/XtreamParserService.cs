using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

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

            using var client = new HttpClient();
            
            var catsResponse = await client.GetAsync(catsUrl);
            catsResponse.EnsureSuccessStatusCode();
            var catsJson = await catsResponse.Content.ReadAsStringAsync();
            using var catsDoc = JsonDocument.Parse(catsJson);

            var streamsResponse = await client.GetAsync(streamsUrl);
            streamsResponse.EnsureSuccessStatusCode();
            var streamsJson = await streamsResponse.Content.ReadAsStringAsync();
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

                            channelsList.Add(new Channel
                            {
                                ChannelCategoryId = mappedCat.Id,
                                Name = name ?? "Unknown",
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
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                
                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = 500;
                    syncState.ErrorLog = $"Xtream parsing failed: {ex.Message}";
                    await db.SaveChangesAsync();
                }
                
                throw new Exception($"Failed to parse Xtream layout: {ex.Message}");
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
            
            using var client = new HttpClient();

            var movCatsJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_vod_categories");
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

            var moviesJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_vod_streams");
            using var moviesDoc = JsonDocument.Parse(moviesJson);
            var parsedMovies = new List<Movie>();

            if (moviesDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in moviesDoc.RootElement.EnumerateArray())
                {
                    string streamId = element.TryGetProperty("stream_id", out var sProp) ? (sProp.ValueKind == JsonValueKind.Number ? sProp.GetInt32().ToString() : sProp.GetString()) : null;
                    string catId = element.TryGetProperty("category_id", out var cProp) ? (cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt32().ToString() : cProp.GetString()) : null;

                    if (string.IsNullOrEmpty(streamId)) continue;
                    
                    var ext = element.TryGetProperty("container_extension", out var exProp) ? exProp.GetString() : "mp4";
                    var name = element.TryGetProperty("name", out var nProp) ? nProp.GetString() : "Unknown";
                    var logo = element.TryGetProperty("stream_icon", out var lProp) ? lProp.GetString() : string.Empty;

                    md.TryGetValue(catId ?? "", out var mappedCatName);

                    parsedMovies.Add(new Movie
                    {
                        SourceProfileId = sourceProfileId,
                        Title = name ?? "Unknown",
                        StreamUrl = $"{baseUrl}/movie/{cred.Username}/{cred.Password}/{streamId}.{ext}",
                        PosterUrl = logo ?? string.Empty,
                        CategoryName = mappedCatName ?? "Uncategorized"
                    });
                }
            }

            var serCatsJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_series_categories");
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

            var seriesJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_series");
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

                    pendingSeries.Add((seriesId, new Series
                    {
                        SourceProfileId = sourceProfileId,
                        Title = name ?? "Unknown",
                        PosterUrl = cover ?? string.Empty,
                        CategoryName = mappedCatName ?? "Uncategorized",
                        Seasons = new List<Season>()
                    }));
                }
            }

            var limitedSeries = pendingSeries.Take(30).ToList();
            var semaphore = new System.Threading.SemaphoreSlim(8);
            
            var seriesTasks = limitedSeries.Select(async sInfo =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var infoStr = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_series_info&series_id={sInfo.SeriesId}");
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
                                        Title = epName ?? "Episode " + epNum,
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

            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var oldMovies = await db.Movies.Where(m => m.SourceProfileId == sourceProfileId).ToListAsync();
                var oldFavMov = await db.Favorites.Where(f => f.ContentType == FavoriteType.Movie && oldMovies.Select(m => m.Id).Contains(f.ContentId)).ToListAsync();
                db.Favorites.RemoveRange(oldFavMov);
                db.Movies.RemoveRange(oldMovies);

                var oldSeries = await db.Series.Include(s => s.Seasons).ThenInclude(sn => sn.Episodes).Where(s => s.SourceProfileId == sourceProfileId).ToListAsync();
                var oldSerFav = await db.Favorites.Where(f => f.ContentType == FavoriteType.Series && oldSeries.Select(s => s.Id).Contains(f.ContentId)).ToListAsync();
                db.Favorites.RemoveRange(oldSerFav);

                foreach (var ser in oldSeries)
                {
                    if (ser.Seasons != null)
                    {
                        foreach (var sn in ser.Seasons)
                        {
                            if (sn.Episodes != null) db.Episodes.RemoveRange(sn.Episodes);
                        }
                        db.Seasons.RemoveRange(ser.Seasons);
                    }
                }
                db.Series.RemoveRange(oldSeries);
                await db.SaveChangesAsync();

                db.Movies.AddRange(parsedMovies);
                db.Series.AddRange(limitedSeries.Select(s => s.BaseObj));
                await db.SaveChangesAsync();

                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = 200;
                    syncState.ErrorLog = $"Xtream VOD Sync: Imported {parsedMovies.Count} movies and {limitedSeries.Count} series.";
                }
                
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                
                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = DateTime.UtcNow;
                    syncState.HttpStatusCode = 500;
                    syncState.ErrorLog = $"Xtream VOD parsing failed: {ex.Message}";
                    await db.SaveChangesAsync();
                }
                
                throw new Exception($"Failed to parse Xtream VOD: {ex.Message}");
            }
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Metadata
{
    public interface ITmdbMetadataService
    {
        Task<bool> HasCredentialAsync(AppDbContext db);
        Task BackfillMissingMetadataAsync(AppDbContext db, int maxMovies = 120, int maxSeries = 80);
        Task EnrichMoviesAsync(AppDbContext db, IEnumerable<Movie> movies, int maxItems = 24);
        Task EnrichSeriesAsync(AppDbContext db, IEnumerable<Series> series, int maxItems = 24);
    }

    public sealed class TmdbMetadataService : ITmdbMetadataService
    {
        private const string ApiBaseUrl = "https://api.themoviedb.org/3";
        private const string ImageBaseUrl = "https://image.tmdb.org/t/p";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);
        private static readonly TimeSpan RecentFailureTtl = TimeSpan.FromHours(6);
        private static readonly TimeSpan NotFoundTtl = TimeSpan.FromDays(7);
        private static readonly TimeSpan CircuitOpenTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
        private const int MaxRequestAttempts = 2;
        private const int BroadFailureCircuitThreshold = 6;
        private static readonly Regex BracketPattern = new Regex(@"\[[^\]]*\]|\([^\)]*\)|\{[^\}]*\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex YearPattern = new Regex(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
        private static readonly Regex EpisodePattern = new Regex(@"\b(s\d{1,2}\s*e\d{1,3}|s\d{1,2}|season\s*\d+|sezon\s*\d+|episode\s*\d+|ep\s*\d+|bolum\s*\d+|bölüm\s*\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NoiseTokenPattern = new Regex(
            @"\b(TR|TURK|TURKIYE|TÜRK|TÜRKİYE|YERLI|YERLİ|DUAL|HD|UHD|SD|FHD|4K|480P|720P|1080P|2160P|HDR|HDR10|HEVC|H264|H265|X264|X265|AAC|DTS|DDP|MULTI|DUBBED|SUBBED|DUBLAJ|ALTYAZI|WEB\s*DL|WEB\s*RIP|WEBDL|WEBRIP|BLU\s*RAY|BLURAY|BRRIP|BDRIP|HDTV|DVDRIP|FRAGMAN|TRAILER|TEASER|CLIP|FEED|CDN|VOD|IPTV|NETFLIX|AMAZON|DISNEY|HBO|EXXEN|GAIN|PUHU|TABII|TV\+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ProviderPrefixPattern = new Regex(@"^\s*(?:TR|TURK|TURKIYE|TÜRK|TÜRKİYE|YERLI|YERLİ|DUAL|HD|UHD|4K|VOD)\s*[:|\-._/]+\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ImdbIdPattern = new Regex(@"\btt\d{6,10}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TmdbIdPattern = new Regex(@"\b\d{2,9}\b", RegexOptions.Compiled);

        private readonly HttpClient _httpClient;

        public TmdbMetadataService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        }

        public async Task<bool> HasCredentialAsync(AppDbContext db)
        {
            return !string.IsNullOrWhiteSpace(await GetApiKeyAsync(db));
        }

        public async Task BackfillMissingMetadataAsync(AppDbContext db, int maxMovies = 120, int maxSeries = 80)
        {
            var staleBefore = DateTime.UtcNow - CacheTtl;
            var missingMovies = await db.Movies
                .Where(m => m.StreamUrl != string.Empty &&
                    (m.MetadataUpdatedAt == null || m.MetadataUpdatedAt < staleBefore) &&
                    (m.TmdbId == null || m.TmdbId == string.Empty ||
                     m.PosterUrl == null || m.PosterUrl == string.Empty ||
                     m.BackdropUrl == null || m.BackdropUrl == string.Empty ||
                     m.Overview == null || m.Overview == string.Empty ||
                     m.VoteAverage <= 0))
                .OrderByDescending(m => m.TmdbId != string.Empty)
                .ThenByDescending(m => m.PosterUrl != string.Empty)
                .ThenBy(m => m.Title)
                .Take(maxMovies)
                .ToListAsync();

            if (missingMovies.Count > 0)
            {
                await EnrichMoviesAsync(db, missingMovies, maxMovies);
            }

            var missingSeries = await db.Series
                .Where(s =>
                    (s.MetadataUpdatedAt == null || s.MetadataUpdatedAt < staleBefore) &&
                    (s.TmdbId == null || s.TmdbId == string.Empty ||
                     s.PosterUrl == null || s.PosterUrl == string.Empty ||
                     s.BackdropUrl == null || s.BackdropUrl == string.Empty ||
                     s.Overview == null || s.Overview == string.Empty ||
                     s.VoteAverage <= 0))
                .OrderByDescending(s => s.TmdbId != string.Empty)
                .ThenByDescending(s => s.PosterUrl != string.Empty)
                .ThenBy(s => s.Title)
                .Take(maxSeries)
                .ToListAsync();

            if (missingSeries.Count > 0)
            {
                await EnrichSeriesAsync(db, missingSeries, maxSeries);
            }
        }

        public async Task EnrichMoviesAsync(AppDbContext db, IEnumerable<Movie> movies, int maxItems = 24)
        {
            var apiKey = await GetApiKeyAsync(db);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                await WriteDiagnosticsAsync(db, "Movie", new TmdbDiagnostics { MissingCredential = true }, 0, 0);
                return;
            }

            var circuitOpenUntil = await GetCircuitOpenUntilAsync(db, "Movie");
            if (circuitOpenUntil > DateTime.UtcNow)
            {
                var circuitDiagnostics = new TmdbDiagnostics
                {
                    CircuitOpen = true,
                    CircuitOpenUntilUtc = circuitOpenUntil
                };
                Debug.WriteLine($"TMDb Movie skipped: circuit open until {circuitOpenUntil:O}");
                await WriteDiagnosticsAsync(db, "Movie", circuitDiagnostics, 0, 0);
                return;
            }

            var inputMovies = movies.ToList();
            var inputIds = inputMovies.Where(m => m.Id > 0).Select(m => m.Id).Distinct().ToList();
            var trackedMovies = inputIds.Count > 0
                ? await db.Movies.Where(m => inputIds.Contains(m.Id)).ToListAsync()
                : inputMovies;

            var diagnostics = new TmdbDiagnostics();
            var candidates = trackedMovies
                .Where(ShouldRefresh)
                .OrderByDescending(m => HasAnyProviderPoster(m.PosterUrl))
                .ThenByDescending(m => m.Popularity)
                .ThenBy(m => m.Title)
                .Take(Math.Max(maxItems * 3, maxItems))
                .ToList();

            foreach (var movie in candidates)
            {
                if (diagnostics.Attempted >= maxItems)
                {
                    break;
                }

                if (await IsItemSuppressedAsync(db, "Movie", movie.Id, movie.Title, diagnostics))
                {
                    continue;
                }

                try
                {
                    diagnostics.Attempted++;
                    var details = await MatchMovieAsync(apiKey, movie, diagnostics);

                    if (details == null)
                    {
                        diagnostics.Missed++;
                        Debug.WriteLine($"TMDb movie miss: id={movie.Id}, title='{movie.Title}', retry=no, nextRetryUtc={(DateTime.UtcNow + NotFoundTtl):O}");
                        await MarkItemFailureAsync(db, "Movie", movie.Id, movie.Title, TmdbFailureKind.NotFound, null, "No TMDb match", false, NotFoundTtl);
                        movie.MetadataUpdatedAt = DateTime.UtcNow;
                        continue;
                    }

                    diagnostics.Matched++;
                    await ClearItemFailureAsync(db, "Movie", movie.Id);
                    ApplyMovie(movie, details);
                }
                catch (TmdbRequestException ex)
                {
                    diagnostics.Errors++;
                    diagnostics.RecordFailure(ex.Kind);
                    Debug.WriteLine(FormatItemFailureLog("Movie", movie.Id, movie.Title, ex));
                    await MarkItemFailureAsync(db, "Movie", movie.Id, movie.Title, ex.Kind, ex.StatusCode, GetExceptionMessage(ex), ex.Retryable, ex.NextRetryUtc - DateTime.UtcNow);
                    movie.MetadataUpdatedAt = DateTime.UtcNow;

                    if (ShouldOpenCircuit(diagnostics))
                    {
                        await OpenCircuitAsync(db, "Movie", diagnostics);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Errors++;
                    diagnostics.RecordFailure(TmdbFailureKind.Unexpected);
                    var nextRetryUtc = DateTime.UtcNow + RecentFailureTtl;
                    Debug.WriteLine($"TMDb Movie failure: id={movie.Id}, title='{movie.Title}', request=unknown, kind=Unexpected, status=none, message='{GetExceptionMessage(ex)}', retry=no, nextRetryUtc={nextRetryUtc:O}");
                    await MarkItemFailureAsync(db, "Movie", movie.Id, movie.Title, TmdbFailureKind.Unexpected, null, GetExceptionMessage(ex), false, RecentFailureTtl);
                    movie.MetadataUpdatedAt = DateTime.UtcNow;
                }
            }

            var savedChanges = 0;
            if (candidates.Count > 0)
            {
                savedChanges = await db.SaveChangesAsync();
            }

            var candidateIds = candidates.Select(m => m.Id).ToList();
            var persistedRows = candidateIds.Count == 0
                ? 0
                : await db.Movies.CountAsync(m =>
                    candidateIds.Contains(m.Id) &&
                    m.TmdbId != string.Empty &&
                    (m.TmdbPosterPath != string.Empty ||
                     m.TmdbBackdropPath != string.Empty ||
                     m.Overview != string.Empty ||
                     m.VoteAverage > 0));

            await WriteDiagnosticsAsync(db, "Movie", diagnostics, persistedRows, savedChanges);
        }

        public async Task EnrichSeriesAsync(AppDbContext db, IEnumerable<Series> series, int maxItems = 24)
        {
            var apiKey = await GetApiKeyAsync(db);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                await WriteDiagnosticsAsync(db, "Series", new TmdbDiagnostics { MissingCredential = true }, 0, 0);
                return;
            }

            var circuitOpenUntil = await GetCircuitOpenUntilAsync(db, "Series");
            if (circuitOpenUntil > DateTime.UtcNow)
            {
                var circuitDiagnostics = new TmdbDiagnostics
                {
                    CircuitOpen = true,
                    CircuitOpenUntilUtc = circuitOpenUntil
                };
                Debug.WriteLine($"TMDb Series skipped: circuit open until {circuitOpenUntil:O}");
                await WriteDiagnosticsAsync(db, "Series", circuitDiagnostics, 0, 0);
                return;
            }

            var inputSeries = series.ToList();
            var inputIds = inputSeries.Where(s => s.Id > 0).Select(s => s.Id).Distinct().ToList();
            var trackedSeries = inputIds.Count > 0
                ? await db.Series.Where(s => inputIds.Contains(s.Id)).ToListAsync()
                : inputSeries;

            var diagnostics = new TmdbDiagnostics();
            var candidates = trackedSeries
                .Where(ShouldRefresh)
                .OrderByDescending(s => HasAnyProviderPoster(s.PosterUrl))
                .ThenByDescending(s => s.Popularity)
                .ThenBy(s => s.Title)
                .Take(Math.Max(maxItems * 3, maxItems))
                .ToList();

            foreach (var show in candidates)
            {
                if (diagnostics.Attempted >= maxItems)
                {
                    break;
                }

                if (await IsItemSuppressedAsync(db, "Series", show.Id, show.Title, diagnostics))
                {
                    continue;
                }

                try
                {
                    diagnostics.Attempted++;
                    var details = await MatchSeriesAsync(apiKey, show, diagnostics);

                    if (details == null)
                    {
                        diagnostics.Missed++;
                        Debug.WriteLine($"TMDb series miss: id={show.Id}, title='{show.Title}', retry=no, nextRetryUtc={(DateTime.UtcNow + NotFoundTtl):O}");
                        await MarkItemFailureAsync(db, "Series", show.Id, show.Title, TmdbFailureKind.NotFound, null, "No TMDb match", false, NotFoundTtl);
                        show.MetadataUpdatedAt = DateTime.UtcNow;
                        continue;
                    }

                    diagnostics.Matched++;
                    await ClearItemFailureAsync(db, "Series", show.Id);
                    ApplySeries(show, details);
                }
                catch (TmdbRequestException ex)
                {
                    diagnostics.Errors++;
                    diagnostics.RecordFailure(ex.Kind);
                    Debug.WriteLine(FormatItemFailureLog("Series", show.Id, show.Title, ex));
                    await MarkItemFailureAsync(db, "Series", show.Id, show.Title, ex.Kind, ex.StatusCode, GetExceptionMessage(ex), ex.Retryable, ex.NextRetryUtc - DateTime.UtcNow);
                    show.MetadataUpdatedAt = DateTime.UtcNow;

                    if (ShouldOpenCircuit(diagnostics))
                    {
                        await OpenCircuitAsync(db, "Series", diagnostics);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Errors++;
                    diagnostics.RecordFailure(TmdbFailureKind.Unexpected);
                    var nextRetryUtc = DateTime.UtcNow + RecentFailureTtl;
                    Debug.WriteLine($"TMDb Series failure: id={show.Id}, title='{show.Title}', request=unknown, kind=Unexpected, status=none, message='{GetExceptionMessage(ex)}', retry=no, nextRetryUtc={nextRetryUtc:O}");
                    await MarkItemFailureAsync(db, "Series", show.Id, show.Title, TmdbFailureKind.Unexpected, null, GetExceptionMessage(ex), false, RecentFailureTtl);
                    show.MetadataUpdatedAt = DateTime.UtcNow;
                }
            }

            var savedChanges = 0;
            if (candidates.Count > 0)
            {
                savedChanges = await db.SaveChangesAsync();
            }

            var candidateIds = candidates.Select(s => s.Id).ToList();
            var persistedRows = candidateIds.Count == 0
                ? 0
                : await db.Series.CountAsync(s =>
                    candidateIds.Contains(s.Id) &&
                    s.TmdbId != string.Empty &&
                    (s.TmdbPosterPath != string.Empty ||
                     s.TmdbBackdropPath != string.Empty ||
                     s.Overview != string.Empty ||
                     s.VoteAverage > 0));

            await WriteDiagnosticsAsync(db, "Series", diagnostics, persistedRows, savedChanges);
        }

        private static bool ShouldRefresh(Movie movie)
        {
            if (movie.MetadataUpdatedAt.HasValue &&
                DateTime.UtcNow - movie.MetadataUpdatedAt.Value < RecentFailureTtl &&
                string.IsNullOrWhiteSpace(movie.TmdbId))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(movie.TmdbId)
                || string.IsNullOrWhiteSpace(movie.Overview)
                || string.IsNullOrWhiteSpace(movie.TmdbBackdropPath)
                || movie.VoteAverage <= 0
                || movie.MetadataUpdatedAt == null
                || DateTime.UtcNow - movie.MetadataUpdatedAt.Value > CacheTtl;
        }

        private static bool ShouldRefresh(Series series)
        {
            if (series.MetadataUpdatedAt.HasValue &&
                DateTime.UtcNow - series.MetadataUpdatedAt.Value < RecentFailureTtl &&
                string.IsNullOrWhiteSpace(series.TmdbId))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(series.TmdbId)
                || string.IsNullOrWhiteSpace(series.Overview)
                || string.IsNullOrWhiteSpace(series.TmdbBackdropPath)
                || series.VoteAverage <= 0
                || series.MetadataUpdatedAt == null
                || DateTime.UtcNow - series.MetadataUpdatedAt.Value > CacheTtl;
        }

        private static bool HasAnyProviderPoster(string posterUrl)
        {
            return !string.IsNullOrWhiteSpace(posterUrl);
        }

        private static async Task WriteDiagnosticsAsync(AppDbContext db, string mediaType, TmdbDiagnostics diagnostics, int persistedRows, int savedChanges)
        {
            var prefix = $"Diagnostics.TMDb.{mediaType}.LastRun";
            var values = new Dictionary<string, string>
            {
                [$"{prefix}.Utc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                [$"{prefix}.MissingCredential"] = diagnostics.MissingCredential ? "1" : "0",
                [$"{prefix}.Attempted"] = diagnostics.Attempted.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.Matched"] = diagnostics.Matched.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.Missed"] = diagnostics.Missed.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.Errors"] = diagnostics.Errors.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.PersistedRows"] = persistedRows.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.SavedChanges"] = savedChanges.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.SearchRequests"] = diagnostics.SearchRequests.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.DetailsRequests"] = diagnostics.DetailsRequests.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.FindRequests"] = diagnostics.FindRequests.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.ProviderTmdbIdAttempts"] = diagnostics.ProviderTmdbIdAttempts.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.ProviderImdbIdAttempts"] = diagnostics.ProviderImdbIdAttempts.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.Suppressed"] = diagnostics.Suppressed.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.CircuitOpen"] = diagnostics.CircuitOpen ? "1" : "0",
                [$"{prefix}.CircuitOpenUntilUtc"] = diagnostics.CircuitOpenUntilUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                [$"{prefix}.NetworkFailures"] = diagnostics.NetworkFailures.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.TimeoutFailures"] = diagnostics.TimeoutFailures.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.AuthFailures"] = diagnostics.AuthFailures.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.RateLimitFailures"] = diagnostics.RateLimitFailures.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.NotFoundFailures"] = diagnostics.NotFoundFailures.ToString(CultureInfo.InvariantCulture),
                [$"{prefix}.ServerFailures"] = diagnostics.ServerFailures.ToString(CultureInfo.InvariantCulture)
            };

            foreach (var item in values)
            {
                var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == item.Key);
                if (setting == null)
                {
                    db.AppSettings.Add(new AppSetting { Key = item.Key, Value = item.Value });
                }
                else
                {
                    setting.Value = item.Value;
                }
            }

            await db.SaveChangesAsync();
            Debug.WriteLine($"TMDb {mediaType}: attempted={diagnostics.Attempted}, matched={diagnostics.Matched}, persisted={persistedRows}, search={diagnostics.SearchRequests}, details={diagnostics.DetailsRequests}, find={diagnostics.FindRequests}, errors={diagnostics.Errors}, suppressed={diagnostics.Suppressed}, circuitOpen={diagnostics.CircuitOpen}");
        }

        private static async Task<DateTime?> GetCircuitOpenUntilAsync(AppDbContext db, string mediaType)
        {
            var value = await db.AppSettings
                .Where(s => s.Key == GetCircuitKey(mediaType) || s.Key == GetCircuitKey("All"))
                .OrderByDescending(s => s.Key == GetCircuitKey(mediaType))
                .Select(s => s.Value)
                .FirstOrDefaultAsync();

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var untilUtc)
                ? untilUtc.ToUniversalTime()
                : null;
        }

        private static async Task<bool> IsItemSuppressedAsync(AppDbContext db, string mediaType, int id, string title, TmdbDiagnostics diagnostics)
        {
            var key = GetItemKey(mediaType, id, "NextRetryUtc");
            var value = await db.AppSettings
                .Where(s => s.Key == key)
                .Select(s => s.Value)
                .FirstOrDefaultAsync();

            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var nextRetryUtc) ||
                nextRetryUtc.ToUniversalTime() <= DateTime.UtcNow)
            {
                return false;
            }

            diagnostics.Suppressed++;
            Debug.WriteLine($"TMDb {mediaType} suppressed: id={id}, title='{title}', nextRetryUtc={nextRetryUtc.ToUniversalTime():O}");
            return true;
        }

        private static async Task MarkItemFailureAsync(
            AppDbContext db,
            string mediaType,
            int id,
            string title,
            TmdbFailureKind kind,
            HttpStatusCode? statusCode,
            string message,
            bool retryable,
            TimeSpan cooldown)
        {
            var safeCooldown = cooldown <= TimeSpan.Zero ? RecentFailureTtl : cooldown;
            var nextRetryUtc = DateTime.UtcNow + safeCooldown;
            await UpsertSettingAsync(db, GetItemKey(mediaType, id, "Title"), title);
            await UpsertSettingAsync(db, GetItemKey(mediaType, id, "LastFailureUtc"), DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await UpsertSettingAsync(db, GetItemKey(mediaType, id, "NextRetryUtc"), nextRetryUtc.ToString("O", CultureInfo.InvariantCulture));
            await UpsertSettingAsync(db, GetItemKey(mediaType, id, "FailureKind"), kind.ToString());
            await UpsertSettingAsync(db, GetItemKey(mediaType, id, "StatusCode"), statusCode.HasValue ? ((int)statusCode.Value).ToString(CultureInfo.InvariantCulture) : string.Empty);
            await UpsertSettingAsync(db, GetItemKey(mediaType, id, "Retryable"), retryable ? "1" : "0");
            await UpsertSettingAsync(db, GetItemKey(mediaType, id, "Message"), TrimForDiagnostics(message));
        }

        private static async Task ClearItemFailureAsync(AppDbContext db, string mediaType, int id)
        {
            var prefix = $"Diagnostics.TMDb.{mediaType}.Item.{id}.";
            var settings = await db.AppSettings
                .Where(s => s.Key.StartsWith(prefix))
                .ToListAsync();

            if (settings.Count > 0)
            {
                db.AppSettings.RemoveRange(settings);
            }
        }

        private static bool ShouldOpenCircuit(TmdbDiagnostics diagnostics)
        {
            return diagnostics.BroadFailures >= BroadFailureCircuitThreshold;
        }

        private static async Task OpenCircuitAsync(AppDbContext db, string mediaType, TmdbDiagnostics diagnostics)
        {
            var openUntilUtc = DateTime.UtcNow + CircuitOpenTtl;
            diagnostics.CircuitOpen = true;
            diagnostics.CircuitOpenUntilUtc = openUntilUtc;
            await UpsertSettingAsync(db, GetCircuitKey(mediaType), openUntilUtc.ToString("O", CultureInfo.InvariantCulture));
            Debug.WriteLine($"TMDb {mediaType} circuit opened: broadFailures={diagnostics.BroadFailures}, openUntilUtc={openUntilUtc:O}");
        }

        private static async Task UpsertSettingAsync(AppDbContext db, string key, string value)
        {
            var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                db.AppSettings.Add(new AppSetting { Key = key, Value = value });
            }
            else
            {
                setting.Value = value;
            }
        }

        private static string GetCircuitKey(string mediaType)
        {
            return $"Diagnostics.TMDb.{mediaType}.CircuitOpenUntilUtc";
        }

        private static string GetItemKey(string mediaType, int id, string field)
        {
            return $"Diagnostics.TMDb.{mediaType}.Item.{id}.{field}";
        }

        private static string FormatItemFailureLog(string mediaType, int id, string title, TmdbRequestException ex)
        {
            var status = ex.StatusCode.HasValue ? ((int)ex.StatusCode.Value).ToString(CultureInfo.InvariantCulture) : "none";
            return $"TMDb {mediaType} failure: id={id}, title='{title}', request={ex.RequestType}, kind={ex.Kind}, status={status}, message='{GetExceptionMessage(ex)}', retry={(ex.Retryable ? "yes" : "no")}, nextRetryUtc={ex.NextRetryUtc:O}";
        }

        private static string GetExceptionMessage(Exception ex)
        {
            var inner = ex.InnerException == null ? string.Empty : $" | inner={ex.InnerException.Message}";
            return TrimForDiagnostics($"{ex.Message}{inner}");
        }

        private static string TrimForDiagnostics(string value)
        {
            var sanitized = Regex.Replace(value ?? string.Empty, @"api_key=[^&\s]+", "api_key=<redacted>", RegexOptions.IgnoreCase);
            return sanitized.Length <= 360 ? sanitized : sanitized.Substring(0, 360);
        }

        private async Task<string> GetApiKeyAsync(AppDbContext db)
        {
            var configured = await db.AppSettings
                .Where(s => s.Key == "Tmdb.ApiKey" || s.Key == "TMDB_API_KEY")
                .OrderByDescending(s => s.Key == "Tmdb.ApiKey")
                .Select(s => s.Value)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            return Environment.GetEnvironmentVariable("KROIRA_TMDB_API_KEY", EnvironmentVariableTarget.Process)
                ?? Environment.GetEnvironmentVariable("KROIRA_TMDB_API_KEY", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("KROIRA_TMDB_API_KEY", EnvironmentVariableTarget.Machine)
                ?? Environment.GetEnvironmentVariable("TMDB_API_KEY", EnvironmentVariableTarget.Process)
                ?? Environment.GetEnvironmentVariable("TMDB_API_KEY", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("TMDB_API_KEY", EnvironmentVariableTarget.Machine)
                ?? string.Empty;
        }

        private async Task<TmdbDetails?> MatchMovieAsync(string apiKey, Movie movie, TmdbDiagnostics diagnostics)
        {
            var context = new TmdbRequestContext("Movie", movie.Id, movie.Title);
            var providerTmdbId = NormalizeTmdbId(movie.TmdbId);
            if (!string.IsNullOrWhiteSpace(providerTmdbId))
            {
                diagnostics.ProviderTmdbIdAttempts++;
                var details = await LoadDetailsAsync(apiKey, "movie", providerTmdbId, diagnostics, context, "details:provider-tmdb-id");
                if (details != null)
                {
                    return details;
                }
            }

            var providerImdbId = NormalizeImdbId(movie.ImdbId) ?? NormalizeImdbId(movie.ExternalId);
            if (!string.IsNullOrWhiteSpace(providerImdbId))
            {
                diagnostics.ProviderImdbIdAttempts++;
                var details = await FindByImdbIdAsync(apiKey, "movie", providerImdbId, diagnostics, context);
                if (details != null)
                {
                    return details;
                }
            }

            return await SearchAndLoadAsync(apiKey, "movie", movie.Title, movie.ReleaseDate?.Year, diagnostics, context);
        }

        private async Task<TmdbDetails?> MatchSeriesAsync(string apiKey, Series series, TmdbDiagnostics diagnostics)
        {
            var context = new TmdbRequestContext("Series", series.Id, series.Title);
            var providerTmdbId = NormalizeTmdbId(series.TmdbId);
            if (!string.IsNullOrWhiteSpace(providerTmdbId))
            {
                diagnostics.ProviderTmdbIdAttempts++;
                var details = await LoadDetailsAsync(apiKey, "tv", providerTmdbId, diagnostics, context, "details:provider-tmdb-id");
                if (details != null)
                {
                    return details;
                }
            }

            var providerImdbId = NormalizeImdbId(series.ImdbId) ?? NormalizeImdbId(series.ExternalId);
            if (!string.IsNullOrWhiteSpace(providerImdbId))
            {
                diagnostics.ProviderImdbIdAttempts++;
                var details = await FindByImdbIdAsync(apiKey, "tv", providerImdbId, diagnostics, context);
                if (details != null)
                {
                    return details;
                }
            }

            return await SearchAndLoadAsync(apiKey, "tv", series.Title, series.FirstAirDate?.Year, diagnostics, context);
        }

        private async Task<TmdbDetails?> FindByImdbIdAsync(string apiKey, string mediaType, string imdbId, TmdbDiagnostics diagnostics, TmdbRequestContext context)
        {
            diagnostics.FindRequests++;
            var url = $"{ApiBaseUrl}/find/{Uri.EscapeDataString(imdbId)}?api_key={Uri.EscapeDataString(apiKey)}&external_source=imdb_id";
            using var doc = await GetJsonAsync(url, diagnostics, context, "find:imdb-id");
            if (doc == null)
            {
                return null;
            }

            var resultProperty = mediaType == "movie" ? "movie_results" : "tv_results";
            if (!doc.RootElement.TryGetProperty(resultProperty, out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var id = results.EnumerateArray()
                .Select(result => GetString(result, "id"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            return string.IsNullOrWhiteSpace(id) ? null : await LoadDetailsAsync(apiKey, mediaType, id, diagnostics, context, "details:imdb-match");
        }

        private async Task<TmdbDetails?> SearchAndLoadAsync(string apiKey, string mediaType, string rawTitle, int? providerYear, TmdbDiagnostics diagnostics, TmdbRequestContext context)
        {
            var queries = BuildSearchQueries(rawTitle, providerYear).ToList();
            if (queries.Count == 0)
            {
                return null;
            }

            foreach (var query in queries)
            {
                var bestId = await SearchBestIdAsync(apiKey, mediaType, query, diagnostics, context);
                if (string.IsNullOrWhiteSpace(bestId))
                {
                    continue;
                }

                var details = await LoadDetailsAsync(apiKey, mediaType, bestId, diagnostics, context, "details:search-match");
                if (details != null)
                {
                    return details;
                }
            }

            return null;
        }

        private async Task<string?> SearchBestIdAsync(string apiKey, string mediaType, TmdbSearchQuery query, TmdbDiagnostics diagnostics, TmdbRequestContext context)
        {
            foreach (var language in new[] { "tr-TR", "en-US" })
            {
                diagnostics.SearchRequests++;
                var url = $"{ApiBaseUrl}/search/{mediaType}?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query.Title)}&include_adult=false&language={language}";
                if (query.Year.HasValue)
                {
                    url += mediaType == "movie"
                        ? $"&year={query.Year.Value}"
                        : $"&first_air_date_year={query.Year.Value}";
                }

                using var searchDoc = await GetJsonAsync(url, diagnostics, context, "search");
                if (searchDoc == null || !searchDoc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var bestId = PickBestResult(results, query, mediaType);
                if (!string.IsNullOrWhiteSpace(bestId))
                {
                    return bestId;
                }
            }

            return null;
        }

        private async Task<TmdbDetails?> LoadDetailsAsync(string apiKey, string mediaType, string tmdbId, TmdbDiagnostics diagnostics, TmdbRequestContext context, string requestType)
        {
            if (string.IsNullOrWhiteSpace(tmdbId))
            {
                return null;
            }

            diagnostics.DetailsRequests++;
            var append = mediaType == "movie" ? "external_ids" : "external_ids";
            var url = $"{ApiBaseUrl}/{mediaType}/{Uri.EscapeDataString(tmdbId)}?api_key={Uri.EscapeDataString(apiKey)}&append_to_response={append}";
            using var doc = await GetJsonAsync(url, diagnostics, context, requestType);
            if (doc == null)
            {
                return null;
            }

            return ParseDetails(doc.RootElement, mediaType);
        }

        private async Task<JsonDocument?> GetJsonAsync(string url, TmdbDiagnostics diagnostics, TmdbRequestContext context, string requestType)
        {
            for (var attempt = 1; attempt <= MaxRequestAttempts; attempt++)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        var kind = ClassifyStatusCode(response.StatusCode);
                        var retryable = IsRetryable(kind);
                        var nextRetryUtc = DateTime.UtcNow + GetRetryDelay(kind, attempt, response);
                        LogRequestFailure(context, requestType, kind, response.StatusCode, null, retryable && attempt < MaxRequestAttempts, nextRetryUtc);

                        if (retryable && attempt < MaxRequestAttempts)
                        {
                            await Task.Delay(GetRetryDelay(kind, attempt, response));
                            continue;
                        }

                        throw new TmdbRequestException(kind, response.StatusCode, requestType, $"TMDb returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}", retryable, DateTime.UtcNow + GetFailureCooldown(kind, response));
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return null;
                    }

                    try
                    {
                        return JsonDocument.Parse(json);
                    }
                    catch (JsonException ex)
                    {
                        throw new TmdbRequestException(TmdbFailureKind.Parse, response.StatusCode, requestType, "TMDb returned invalid JSON", false, DateTime.UtcNow + GetFailureCooldown(TmdbFailureKind.Parse, response), ex);
                    }
                }
                catch (HttpRequestException ex)
                {
                    var nextRetryUtc = DateTime.UtcNow + GetRetryDelay(TmdbFailureKind.Network, attempt, null);
                    LogRequestFailure(context, requestType, TmdbFailureKind.Network, ex.StatusCode, ex, attempt < MaxRequestAttempts, nextRetryUtc);

                    if (attempt < MaxRequestAttempts)
                    {
                        await Task.Delay(GetRetryDelay(TmdbFailureKind.Network, attempt, null));
                        continue;
                    }

                    throw new TmdbRequestException(TmdbFailureKind.Network, ex.StatusCode, requestType, "TMDb network request failed", true, DateTime.UtcNow + GetFailureCooldown(TmdbFailureKind.Network, null), ex);
                }
                catch (TaskCanceledException ex)
                {
                    var nextRetryUtc = DateTime.UtcNow + GetRetryDelay(TmdbFailureKind.Timeout, attempt, null);
                    LogRequestFailure(context, requestType, TmdbFailureKind.Timeout, null, ex, attempt < MaxRequestAttempts, nextRetryUtc);

                    if (attempt < MaxRequestAttempts)
                    {
                        await Task.Delay(GetRetryDelay(TmdbFailureKind.Timeout, attempt, null));
                        continue;
                    }

                    throw new TmdbRequestException(TmdbFailureKind.Timeout, null, requestType, "TMDb request timed out", true, DateTime.UtcNow + GetFailureCooldown(TmdbFailureKind.Timeout, null), ex);
                }
            }

            return null;
        }

        private static TmdbFailureKind ClassifyStatusCode(HttpStatusCode statusCode)
        {
            var numeric = (int)statusCode;
            return statusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => TmdbFailureKind.Auth,
                HttpStatusCode.NotFound => TmdbFailureKind.NotFound,
                (HttpStatusCode)429 => TmdbFailureKind.RateLimit,
                HttpStatusCode.RequestTimeout => TmdbFailureKind.Timeout,
                _ when numeric >= 500 => TmdbFailureKind.Server,
                _ => TmdbFailureKind.Http
            };
        }

        private static bool IsRetryable(TmdbFailureKind kind)
        {
            return kind is TmdbFailureKind.Network or TmdbFailureKind.Timeout or TmdbFailureKind.RateLimit or TmdbFailureKind.Server;
        }

        private static TimeSpan GetRetryDelay(TmdbFailureKind kind, int attempt, HttpResponseMessage? response)
        {
            if (kind == TmdbFailureKind.NotFound || kind == TmdbFailureKind.Auth)
            {
                return kind == TmdbFailureKind.NotFound ? NotFoundTtl : CircuitOpenTtl;
            }

            if (kind == TmdbFailureKind.RateLimit &&
                response?.Headers.RetryAfter?.Delta is { } retryAfterDelta &&
                retryAfterDelta > TimeSpan.Zero)
            {
                return retryAfterDelta > CircuitOpenTtl ? CircuitOpenTtl : retryAfterDelta;
            }

            var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
            var delay = TimeSpan.FromMilliseconds(InitialRetryDelay.TotalMilliseconds * multiplier);
            return delay > TimeSpan.FromSeconds(8) ? TimeSpan.FromSeconds(8) : delay;
        }

        private static TimeSpan GetFailureCooldown(TmdbFailureKind kind, HttpResponseMessage? response)
        {
            if (kind == TmdbFailureKind.NotFound)
            {
                return NotFoundTtl;
            }

            if (kind == TmdbFailureKind.Auth)
            {
                return CircuitOpenTtl;
            }

            if (kind == TmdbFailureKind.RateLimit &&
                response?.Headers.RetryAfter?.Delta is { } retryAfterDelta &&
                retryAfterDelta > TimeSpan.Zero)
            {
                return retryAfterDelta > CircuitOpenTtl ? CircuitOpenTtl : retryAfterDelta;
            }

            return RecentFailureTtl;
        }

        private static void LogRequestFailure(
            TmdbRequestContext context,
            string requestType,
            TmdbFailureKind kind,
            HttpStatusCode? statusCode,
            Exception? exception,
            bool willRetry,
            DateTime nextRetryUtc)
        {
            var status = statusCode.HasValue ? ((int)statusCode.Value).ToString(CultureInfo.InvariantCulture) : "none";
            var message = exception == null ? string.Empty : GetExceptionMessage(exception);
            Debug.WriteLine($"TMDb request failure: media={context.MediaType}, id={context.ContentId}, title='{context.Title}', request={requestType}, kind={kind}, status={status}, message='{message}', retry={(willRetry ? "yes" : "no")}, nextRetryUtc={nextRetryUtc:O}");
        }

        private static IEnumerable<TmdbSearchQuery> BuildSearchQueries(string rawTitle, int? providerYear)
        {
            var raw = rawTitle ?? string.Empty;
            var year = providerYear ?? ExtractYear(raw);
            var cleaned = CleanProviderTitle(raw);
            var variants = new List<string> { cleaned };

            variants.AddRange(BuildAlternateTitleVariants(cleaned));
            variants.Add(RemoveTurkishDiacritics(cleaned));

            foreach (var title in variants
                         .Select(NormalizeSpacing)
                         .Where(title => title.Length >= 2 && !LooksLikeNonFeature(title))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (year.HasValue)
                {
                    yield return new TmdbSearchQuery(title, year, true);
                }

                yield return new TmdbSearchQuery(title, null, false);
            }
        }

        private static string? PickBestResult(JsonElement results, TmdbSearchQuery query, string mediaType)
        {
            string? fallbackId = null;
            double fallbackScore = double.MinValue;
            var normalizedQuery = NormalizeForCompare(query.Title);

            foreach (var result in results.EnumerateArray().Take(8))
            {
                var id = GetString(result, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var title = mediaType == "movie"
                    ? GetString(result, "title") ?? GetString(result, "original_title")
                    : GetString(result, "name") ?? GetString(result, "original_name");
                var date = mediaType == "movie"
                    ? GetString(result, "release_date")
                    : GetString(result, "first_air_date");
                var popularity = GetDouble(result, "popularity");
                var voteCount = GetDouble(result, "vote_count");
                var originalTitle = mediaType == "movie"
                    ? GetString(result, "original_title")
                    : GetString(result, "original_name");

                if (LooksLikeNonFeature(title ?? string.Empty) || LooksLikeNonFeature(originalTitle ?? string.Empty))
                {
                    continue;
                }

                var titleScore = Math.Max(
                    CalculateTitleScore(normalizedQuery, NormalizeForCompare(title ?? string.Empty)),
                    CalculateTitleScore(normalizedQuery, NormalizeForCompare(originalTitle ?? string.Empty)));

                if (titleScore < 70)
                {
                    continue;
                }

                var yearScore = 0d;
                var hasBadYearMismatch = false;
                if (query.Year.HasValue && TryParseYear(date, out var resultYear))
                {
                    var delta = Math.Abs(resultYear - query.Year.Value);
                    if (delta == 0)
                    {
                        yearScore = 18;
                    }
                    else if (delta == 1)
                    {
                        yearScore = 7;
                    }
                    else
                    {
                        yearScore = mediaType == "movie" ? -26 : -14;
                        hasBadYearMismatch = true;
                    }
                }

                if (hasBadYearMismatch && titleScore < 96)
                {
                    continue;
                }

                if (!query.Year.HasValue && titleScore < 84)
                {
                    continue;
                }

                var score = titleScore
                    + yearScore
                    + Math.Min(popularity / 12, 8)
                    + Math.Min(voteCount / 1000, 4);

                if (score > fallbackScore)
                {
                    fallbackScore = score;
                    fallbackId = id;
                }
            }

            var threshold = query.Year.HasValue ? 82 : 88;
            return fallbackScore >= threshold ? fallbackId : null;
        }

        private static string CleanProviderTitle(string rawTitle)
        {
            var title = rawTitle ?? string.Empty;
            title = title.Replace('&', ' ');
            title = BracketPattern.Replace(title, " ");
            title = EpisodePattern.Replace(title, " ");
            title = YearPattern.Replace(title, " ");

            for (var i = 0; i < 4; i++)
            {
                title = ProviderPrefixPattern.Replace(title, " ");
            }

            title = NoiseTokenPattern.Replace(title, " ");
            title = Regex.Replace(title, @"\b\d{1,2}\s*(?:fps|bit|bitrate)\b", " ", RegexOptions.IgnoreCase);
            title = Regex.Replace(title, @"[_|•·]+", " ");
            title = Regex.Replace(title, @"\s*[-:]+\s*", " ");
            title = Regex.Replace(title, @"[.]{2,}", " ");
            title = Regex.Replace(title, @"\s+", " ");

            return title.Trim(' ', '-', ':', '.', '_', '|', '/', '\\');
        }

        private static IEnumerable<string> BuildAlternateTitleVariants(string title)
        {
            foreach (var separator in new[] { " / ", " | ", " - ", " : " })
            {
                if (!title.Contains(separator, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var part in title.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (part.Length >= 2)
                    {
                        yield return part;
                    }
                }
            }
        }

        private static int? ExtractYear(string value)
        {
            var matches = YearPattern.Matches(value ?? string.Empty);
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Value, out var year) && year >= 1900 && year <= DateTime.UtcNow.Year + 2)
                {
                    return year;
                }
            }

            return null;
        }

        private static bool TryParseYear(string? dateText, out int year)
        {
            year = 0;
            if (string.IsNullOrWhiteSpace(dateText) || dateText.Length < 4)
            {
                return false;
            }

            return int.TryParse(dateText.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out year);
        }

        private static bool LooksLikeNonFeature(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            return Regex.IsMatch(title, @"\b(trailer|fragman|teaser|clip|recap|preview|behind\s+the\s+scenes|kamera\s+arkası|feed|cdn)\b", RegexOptions.IgnoreCase);
        }

        private static string? NormalizeTmdbId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = TmdbIdPattern.Match(value);
            return match.Success ? match.Value : null;
        }

        private static string? NormalizeImdbId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = ImdbIdPattern.Match(value);
            return match.Success ? match.Value.ToLowerInvariant() : null;
        }

        private static string NormalizeSpacing(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        }

        private static double CalculateTitleScore(string normalizedQuery, string normalizedTitle)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery) || string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return 0;
            }

            if (normalizedQuery == normalizedTitle)
            {
                return 100;
            }

            if (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal) ||
                normalizedQuery.Contains(normalizedTitle, StringComparison.Ordinal))
            {
                var shorter = Math.Min(normalizedQuery.Length, normalizedTitle.Length);
                var longer = Math.Max(normalizedQuery.Length, normalizedTitle.Length);
                return 86 + (12d * shorter / longer);
            }

            var editScore = 100d * (1d - (double)LevenshteinDistance(normalizedQuery, normalizedTitle) / Math.Max(normalizedQuery.Length, normalizedTitle.Length));
            var tokenScore = TokenOverlapScore(normalizedQuery, normalizedTitle);
            return Math.Max(editScore, tokenScore);
        }

        private static double TokenOverlapScore(string left, string right)
        {
            var leftTokens = SplitComparableTokens(left);
            var rightTokens = SplitComparableTokens(right);
            if (leftTokens.Count == 0 || rightTokens.Count == 0)
            {
                return 0;
            }

            var overlap = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
            return 100d * overlap / Math.Max(leftTokens.Count, rightTokens.Count);
        }

        private static HashSet<string> SplitComparableTokens(string value)
        {
            return Regex.Matches(value, @"[a-z0-9]+")
                .Select(match => match.Value)
                .Where(token => token.Length > 1)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static int LevenshteinDistance(string left, string right)
        {
            var previous = new int[right.Length + 1];
            var current = new int[right.Length + 1];

            for (var j = 0; j <= right.Length; j++)
            {
                previous[j] = j;
            }

            for (var i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= right.Length; j++)
                {
                    var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                }

                (previous, current) = (current, previous);
            }

            return previous[right.Length];
        }

        private static TmdbDetails ParseDetails(JsonElement root, string mediaType)
        {
            var tmdbId = GetString(root, "id") ?? string.Empty;
            var externalIds = root.TryGetProperty("external_ids", out var ext) ? ext : default;
            var imdbId = externalIds.ValueKind == JsonValueKind.Object ? GetString(externalIds, "imdb_id") : string.Empty;
            var posterPath = GetString(root, "poster_path") ?? string.Empty;
            var backdropPath = GetString(root, "backdrop_path") ?? string.Empty;
            var dateText = mediaType == "movie" ? GetString(root, "release_date") : GetString(root, "first_air_date");

            return new TmdbDetails(
                tmdbId,
                imdbId ?? string.Empty,
                posterPath,
                backdropPath,
                BuildImageUrl(posterPath, "w500"),
                BuildImageUrl(backdropPath, "w1280"),
                GetString(root, "overview") ?? string.Empty,
                ParseGenres(root),
                ParseDate(dateText),
                GetDouble(root, "vote_average"),
                GetDouble(root, "popularity"),
                GetString(root, "original_language") ?? string.Empty);
        }

        private static void ApplyMovie(Movie movie, TmdbDetails details)
        {
            movie.TmdbId = details.TmdbId;
            movie.ImdbId = details.ImdbId;
            movie.TmdbPosterPath = details.PosterPath;
            movie.TmdbBackdropPath = details.BackdropPath;
            movie.PosterUrl = string.IsNullOrWhiteSpace(movie.PosterUrl) ? details.PosterUrl : movie.PosterUrl;
            movie.BackdropUrl = string.IsNullOrWhiteSpace(movie.BackdropUrl) ? details.BackdropUrl : movie.BackdropUrl;
            movie.Overview = string.IsNullOrWhiteSpace(details.Overview) ? movie.Overview : details.Overview;
            movie.Genres = string.IsNullOrWhiteSpace(details.Genres) ? movie.Genres : details.Genres;
            movie.ReleaseDate = details.Date ?? movie.ReleaseDate;
            movie.VoteAverage = details.VoteAverage > 0 ? details.VoteAverage : movie.VoteAverage;
            movie.Popularity = details.Popularity > 0 ? details.Popularity : movie.Popularity;
            movie.OriginalLanguage = string.IsNullOrWhiteSpace(details.OriginalLanguage) ? movie.OriginalLanguage : details.OriginalLanguage;
            movie.MetadataUpdatedAt = DateTime.UtcNow;
        }

        private static void ApplySeries(Series series, TmdbDetails details)
        {
            series.TmdbId = details.TmdbId;
            series.ImdbId = details.ImdbId;
            series.TmdbPosterPath = details.PosterPath;
            series.TmdbBackdropPath = details.BackdropPath;
            series.PosterUrl = string.IsNullOrWhiteSpace(series.PosterUrl) ? details.PosterUrl : series.PosterUrl;
            series.BackdropUrl = string.IsNullOrWhiteSpace(series.BackdropUrl) ? details.BackdropUrl : series.BackdropUrl;
            series.Overview = string.IsNullOrWhiteSpace(details.Overview) ? series.Overview : details.Overview;
            series.Genres = string.IsNullOrWhiteSpace(details.Genres) ? series.Genres : details.Genres;
            series.FirstAirDate = details.Date ?? series.FirstAirDate;
            series.VoteAverage = details.VoteAverage > 0 ? details.VoteAverage : series.VoteAverage;
            series.Popularity = details.Popularity > 0 ? details.Popularity : series.Popularity;
            series.OriginalLanguage = string.IsNullOrWhiteSpace(details.OriginalLanguage) ? series.OriginalLanguage : details.OriginalLanguage;
            series.MetadataUpdatedAt = DateTime.UtcNow;
        }

        private static string ParseGenres(JsonElement root)
        {
            if (!root.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            return string.Join(", ", genres
                .EnumerateArray()
                .Select(g => GetString(g, "name"))
                .Where(g => !string.IsNullOrWhiteSpace(g)));
        }

        private static DateTime? ParseDate(string? value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.Date
                : null;
        }

        private static string BuildImageUrl(string path, string size)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : $"{ImageBaseUrl}/{size}{path}";
        }

        private static string NormalizeForCompare(string value)
        {
            return Regex.Replace(RemoveTurkishDiacritics(value).ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
        }

        private static string RemoveTurkishDiacritics(string value)
        {
            return (value ?? string.Empty)
                .Replace('ı', 'i')
                .Replace('İ', 'I')
                .Replace('ş', 's')
                .Replace('Ş', 'S')
                .Replace('ğ', 'g')
                .Replace('Ğ', 'G')
                .Replace('ü', 'u')
                .Replace('Ü', 'U')
                .Replace('ö', 'o')
                .Replace('Ö', 'O')
                .Replace('ç', 'c')
                .Replace('Ç', 'C');
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                _ => null
            };
        }

        private static double GetDouble(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return 0;
            }

            return property.ValueKind switch
            {
                JsonValueKind.Number => property.GetDouble(),
                JsonValueKind.String when double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
                _ => 0
            };
        }

        private sealed record TmdbSearchQuery(string Title, int? Year, bool IsYearQualified);

        private sealed record TmdbRequestContext(string MediaType, int ContentId, string Title);

        private enum TmdbFailureKind
        {
            Network,
            Timeout,
            Auth,
            RateLimit,
            NotFound,
            Server,
            Http,
            Parse,
            Unexpected
        }

        private sealed class TmdbRequestException : Exception
        {
            public TmdbRequestException(
                TmdbFailureKind kind,
                HttpStatusCode? statusCode,
                string requestType,
                string message,
                bool retryable,
                DateTime nextRetryUtc,
                Exception? innerException = null)
                : base(message, innerException)
            {
                Kind = kind;
                StatusCode = statusCode;
                RequestType = requestType;
                Retryable = retryable;
                NextRetryUtc = nextRetryUtc;
            }

            public TmdbFailureKind Kind { get; }
            public HttpStatusCode? StatusCode { get; }
            public string RequestType { get; }
            public bool Retryable { get; }
            public DateTime NextRetryUtc { get; }
        }

        private sealed class TmdbDiagnostics
        {
            public bool MissingCredential { get; set; }
            public bool CircuitOpen { get; set; }
            public DateTime? CircuitOpenUntilUtc { get; set; }
            public int Attempted { get; set; }
            public int Matched { get; set; }
            public int Missed { get; set; }
            public int Errors { get; set; }
            public int Suppressed { get; set; }
            public int SearchRequests { get; set; }
            public int DetailsRequests { get; set; }
            public int FindRequests { get; set; }
            public int ProviderTmdbIdAttempts { get; set; }
            public int ProviderImdbIdAttempts { get; set; }
            public int NetworkFailures { get; set; }
            public int TimeoutFailures { get; set; }
            public int AuthFailures { get; set; }
            public int RateLimitFailures { get; set; }
            public int NotFoundFailures { get; set; }
            public int ServerFailures { get; set; }
            public int HttpFailures { get; set; }
            public int ParseFailures { get; set; }
            public int UnexpectedFailures { get; set; }

            public int BroadFailures => NetworkFailures + TimeoutFailures + AuthFailures + RateLimitFailures + ServerFailures;

            public void RecordFailure(TmdbFailureKind kind)
            {
                switch (kind)
                {
                    case TmdbFailureKind.Network:
                        NetworkFailures++;
                        break;
                    case TmdbFailureKind.Timeout:
                        TimeoutFailures++;
                        break;
                    case TmdbFailureKind.Auth:
                        AuthFailures++;
                        break;
                    case TmdbFailureKind.RateLimit:
                        RateLimitFailures++;
                        break;
                    case TmdbFailureKind.NotFound:
                        NotFoundFailures++;
                        break;
                    case TmdbFailureKind.Server:
                        ServerFailures++;
                        break;
                    case TmdbFailureKind.Http:
                        HttpFailures++;
                        break;
                    case TmdbFailureKind.Parse:
                        ParseFailures++;
                        break;
                    case TmdbFailureKind.Unexpected:
                        UnexpectedFailures++;
                        break;
                }
            }
        }

        private sealed record TmdbDetails(
            string TmdbId,
            string ImdbId,
            string PosterPath,
            string BackdropPath,
            string PosterUrl,
            string BackdropUrl,
            string Overview,
            string Genres,
            DateTime? Date,
            double VoteAverage,
            double Popularity,
            string OriginalLanguage);
    }
}

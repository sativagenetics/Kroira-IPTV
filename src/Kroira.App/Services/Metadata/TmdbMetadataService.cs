#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private static readonly Regex BracketPattern = new Regex(@"\[[^\]]+\]|\([^\)]*(?:1080p|720p|2160p|4k|hdr|x264|x265|bluray|webrip|web-dl|hdtv)[^\)]*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex YearPattern = new Regex(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
        private static readonly Regex QualityPattern = new Regex(@"\b(480p|720p|1080p|2160p|4k|uhd|hdr|hdtv|web[-\s]?dl|webrip|bluray|x264|x265|hevc|aac|dts|multi|dual|dubbed|subbed)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                return;
            }

            var candidates = movies
                .Where(ShouldRefresh)
                .OrderByDescending(m => HasAnyProviderPoster(m.PosterUrl))
                .ThenByDescending(m => m.Popularity)
                .ThenBy(m => m.Title)
                .Take(maxItems)
                .ToList();

            foreach (var movie in candidates)
            {
                try
                {
                    var details = string.IsNullOrWhiteSpace(movie.TmdbId)
                        ? await SearchAndLoadAsync(apiKey, "movie", movie.Title)
                        : await LoadDetailsAsync(apiKey, "movie", movie.TmdbId);

                    if (details == null)
                    {
                        movie.MetadataUpdatedAt = DateTime.UtcNow;
                        continue;
                    }

                    ApplyMovie(movie, details);
                }
                catch
                {
                    movie.MetadataUpdatedAt = DateTime.UtcNow;
                }
            }

            if (candidates.Count > 0)
            {
                await db.SaveChangesAsync();
            }
        }

        public async Task EnrichSeriesAsync(AppDbContext db, IEnumerable<Series> series, int maxItems = 24)
        {
            var apiKey = await GetApiKeyAsync(db);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return;
            }

            var candidates = series
                .Where(ShouldRefresh)
                .OrderByDescending(s => HasAnyProviderPoster(s.PosterUrl))
                .ThenByDescending(s => s.Popularity)
                .ThenBy(s => s.Title)
                .Take(maxItems)
                .ToList();

            foreach (var show in candidates)
            {
                try
                {
                    var details = string.IsNullOrWhiteSpace(show.TmdbId)
                        ? await SearchAndLoadAsync(apiKey, "tv", show.Title)
                        : await LoadDetailsAsync(apiKey, "tv", show.TmdbId);

                    if (details == null)
                    {
                        show.MetadataUpdatedAt = DateTime.UtcNow;
                        continue;
                    }

                    ApplySeries(show, details);
                }
                catch
                {
                    show.MetadataUpdatedAt = DateTime.UtcNow;
                }
            }

            if (candidates.Count > 0)
            {
                await db.SaveChangesAsync();
            }
        }

        private static bool ShouldRefresh(Movie movie)
        {
            return movie.MetadataUpdatedAt == null
                || DateTime.UtcNow - movie.MetadataUpdatedAt.Value > CacheTtl;
        }

        private static bool ShouldRefresh(Series series)
        {
            return series.MetadataUpdatedAt == null
                || DateTime.UtcNow - series.MetadataUpdatedAt.Value > CacheTtl;
        }

        private static bool HasAnyProviderPoster(string posterUrl)
        {
            return !string.IsNullOrWhiteSpace(posterUrl);
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

        private async Task<TmdbDetails?> SearchAndLoadAsync(string apiKey, string mediaType, string rawTitle)
        {
            var query = BuildSearchQuery(rawTitle);
            if (string.IsNullOrWhiteSpace(query.Title))
            {
                return null;
            }

            var url = $"{ApiBaseUrl}/search/{mediaType}?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query.Title)}&include_adult=false";
            if (query.Year.HasValue)
            {
                url += mediaType == "movie"
                    ? $"&year={query.Year.Value}"
                    : $"&first_air_date_year={query.Year.Value}";
            }

            using var searchDoc = await GetJsonAsync(url);
            if (searchDoc == null || !searchDoc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var bestId = PickBestResult(results, query.Title, query.Year, mediaType);
            return bestId == null ? null : await LoadDetailsAsync(apiKey, mediaType, bestId);
        }

        private async Task<TmdbDetails?> LoadDetailsAsync(string apiKey, string mediaType, string tmdbId)
        {
            if (string.IsNullOrWhiteSpace(tmdbId))
            {
                return null;
            }

            var append = mediaType == "movie" ? "external_ids" : "external_ids";
            var url = $"{ApiBaseUrl}/{mediaType}/{Uri.EscapeDataString(tmdbId)}?api_key={Uri.EscapeDataString(apiKey)}&append_to_response={append}";
            using var doc = await GetJsonAsync(url);
            if (doc == null)
            {
                return null;
            }

            return ParseDetails(doc.RootElement, mediaType);
        }

        private async Task<JsonDocument?> GetJsonAsync(string url)
        {
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json);
        }

        private static TmdbSearchQuery BuildSearchQuery(string rawTitle)
        {
            var title = rawTitle ?? string.Empty;
            var year = YearPattern.Match(title);
            int? parsedYear = year.Success && int.TryParse(year.Value, out var y) ? y : null;

            title = BracketPattern.Replace(title, " ");
            title = YearPattern.Replace(title, " ");
            title = QualityPattern.Replace(title, " ");
            title = title
                .Replace('.', ' ')
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Replace("  ", " ")
                .Trim();

            return new TmdbSearchQuery(title, parsedYear);
        }

        private static string? PickBestResult(JsonElement results, string queryTitle, int? queryYear, string mediaType)
        {
            var normalizedQuery = NormalizeForCompare(queryTitle);
            string? fallbackId = null;
            double fallbackScore = double.MinValue;

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
                var normalizedTitle = NormalizeForCompare(title ?? string.Empty);
                var score = popularity;

                if (normalizedTitle == normalizedQuery)
                {
                    score += 1000;
                }
                else if (normalizedTitle.Contains(normalizedQuery) || normalizedQuery.Contains(normalizedTitle))
                {
                    score += 300;
                }

                if (queryYear.HasValue && date?.StartsWith(queryYear.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) == true)
                {
                    score += 200;
                }

                if (score > fallbackScore)
                {
                    fallbackScore = score;
                    fallbackId = id;
                }
            }

            return fallbackId;
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
            movie.Overview = details.Overview;
            movie.Genres = details.Genres;
            movie.ReleaseDate = details.Date;
            movie.VoteAverage = details.VoteAverage;
            movie.Popularity = details.Popularity;
            movie.OriginalLanguage = details.OriginalLanguage;
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
            series.Overview = details.Overview;
            series.Genres = details.Genres;
            series.FirstAirDate = details.Date;
            series.VoteAverage = details.VoteAverage;
            series.Popularity = details.Popularity;
            series.OriginalLanguage = details.OriginalLanguage;
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
            return Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
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

        private sealed record TmdbSearchQuery(string Title, int? Year);

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

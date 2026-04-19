using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public readonly record struct CatalogFingerprintResult(string CanonicalTitleKey, string DedupFingerprint, bool IsStrong);

    public static class CatalogFingerprinting
    {
        private static readonly Regex TmdbIdRegex = new(@"\b\d{2,9}\b", RegexOptions.Compiled);
        private static readonly Regex ImdbIdRegex = new(@"\btt\d{6,10}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TokenRegex = new(@"[a-z0-9]+", RegexOptions.Compiled);

        public static void Apply(Movie movie)
        {
            var result = ComputeMovie(movie);
            movie.CanonicalTitleKey = result.CanonicalTitleKey;
            movie.DedupFingerprint = result.DedupFingerprint;
        }

        public static void Apply(Series series)
        {
            var result = ComputeSeries(series);
            series.CanonicalTitleKey = result.CanonicalTitleKey;
            series.DedupFingerprint = result.DedupFingerprint;
        }

        public static CatalogFingerprintResult ComputeMovie(Movie movie)
        {
            return Compute(
                mediaPrefix: "movie",
                title: movie.Title,
                contentKind: movie.ContentKind,
                tmdbId: movie.TmdbId,
                imdbId: movie.ImdbId,
                year: movie.ReleaseDate?.Year,
                sourceProfileId: movie.SourceProfileId,
                sourceStableKey: BuildMovieSourceStableKey(movie));
        }

        public static CatalogFingerprintResult ComputeSeries(Series series)
        {
            return Compute(
                mediaPrefix: "series",
                title: series.Title,
                contentKind: series.ContentKind,
                tmdbId: series.TmdbId,
                imdbId: series.ImdbId,
                year: series.FirstAirDate?.Year,
                sourceProfileId: series.SourceProfileId,
                sourceStableKey: BuildSeriesSourceStableKey(series));
        }

        public static bool IsStrongFingerprint(string fingerprint)
        {
            return fingerprint.StartsWith("movie:tmdb:", StringComparison.Ordinal) ||
                   fingerprint.StartsWith("movie:imdb:", StringComparison.Ordinal) ||
                   fingerprint.StartsWith("movie:titleyear:", StringComparison.Ordinal) ||
                   fingerprint.StartsWith("series:tmdb:", StringComparison.Ordinal) ||
                   fingerprint.StartsWith("series:imdb:", StringComparison.Ordinal) ||
                   fingerprint.StartsWith("series:titleyear:", StringComparison.Ordinal);
        }

        private static CatalogFingerprintResult Compute(
            string mediaPrefix,
            string title,
            string contentKind,
            string tmdbId,
            string imdbId,
            int? year,
            int sourceProfileId,
            string sourceStableKey)
        {
            var canonicalTitleKey = NormalizeTitleKey(title);
            var normalizedContentKind = string.IsNullOrWhiteSpace(contentKind) ? "Primary" : contentKind.Trim();
            var normalizedTmdbId = NormalizeTmdbId(tmdbId);
            var normalizedImdbId = NormalizeImdbId(imdbId);
            var normalizedYear = NormalizeYear(year);

            if (!string.IsNullOrWhiteSpace(normalizedTmdbId))
            {
                return new CatalogFingerprintResult(
                    canonicalTitleKey,
                    $"{mediaPrefix}:tmdb:{normalizedContentKind}:{normalizedTmdbId}",
                    true);
            }

            if (!string.IsNullOrWhiteSpace(normalizedImdbId))
            {
                return new CatalogFingerprintResult(
                    canonicalTitleKey,
                    $"{mediaPrefix}:imdb:{normalizedContentKind}:{normalizedImdbId}",
                    true);
            }

            if (!string.IsNullOrWhiteSpace(canonicalTitleKey) &&
                normalizedYear.HasValue &&
                IsTitleStrongEnoughForTitleYear(canonicalTitleKey))
            {
                return new CatalogFingerprintResult(
                    canonicalTitleKey,
                    $"{mediaPrefix}:titleyear:{normalizedContentKind}:{canonicalTitleKey}:{normalizedYear.Value}",
                    true);
            }

            if (!string.IsNullOrWhiteSpace(sourceStableKey))
            {
                return new CatalogFingerprintResult(
                    canonicalTitleKey,
                    $"{mediaPrefix}:source:{normalizedContentKind}:{sourceProfileId}:{sourceStableKey}",
                    false);
            }

            return new CatalogFingerprintResult(canonicalTitleKey, string.Empty, false);
        }

        private static string BuildMovieSourceStableKey(Movie movie)
        {
            if (!string.IsNullOrWhiteSpace(movie.ExternalId))
            {
                return $"ext:{movie.ExternalId.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(movie.StreamUrl))
            {
                return $"url:{NormalizeUrl(movie.StreamUrl)}";
            }

            return string.Empty;
        }

        private static string BuildSeriesSourceStableKey(Series series)
        {
            return string.IsNullOrWhiteSpace(series.ExternalId)
                ? string.Empty
                : $"ext:{series.ExternalId.Trim()}";
        }

        private static string NormalizeTitleKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lower = RemoveDiacritics(value).ToLowerInvariant();
            var tokens = TokenRegex.Matches(lower)
                .Select(match => match.Value)
                .Where(token => token.Length > 0)
                .ToList();

            return string.Join(" ", tokens);
        }

        private static string NormalizeUrl(string value)
        {
            var trimmed = value.Trim();
            var queryIndex = trimmed.IndexOf('?');
            if (queryIndex >= 0)
            {
                trimmed = trimmed[..queryIndex];
            }

            return trimmed.TrimEnd('/').ToLowerInvariant();
        }

        private static string NormalizeTmdbId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var match = TmdbIdRegex.Match(value);
            return match.Success ? match.Value : string.Empty;
        }

        private static string NormalizeImdbId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var match = ImdbIdRegex.Match(value);
            return match.Success ? match.Value.ToLowerInvariant() : string.Empty;
        }

        private static int? NormalizeYear(int? year)
        {
            if (!year.HasValue)
            {
                return null;
            }

            if (year.Value < 1900 || year.Value > DateTime.UtcNow.Year + 2)
            {
                return null;
            }

            return year.Value;
        }

        private static bool IsTitleStrongEnoughForTitleYear(string canonicalTitleKey)
        {
            if (string.IsNullOrWhiteSpace(canonicalTitleKey))
            {
                return false;
            }

            var tokens = canonicalTitleKey.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return canonicalTitleKey.Length >= 7 && tokens.Length >= 2;
        }

        private static string RemoveDiacritics(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}

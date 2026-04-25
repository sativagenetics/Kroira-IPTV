#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kroira.App.Services.Metadata
{
    public enum MetadataMediaKind
    {
        Movie,
        Series
    }

    public sealed record MetadataTitleAnalysis(
        string RawTitle,
        string CleanTitle,
        string NormalizedTitle,
        int? Year,
        int? SeasonNumber,
        int? EpisodeNumber,
        bool LooksLikeEpisode,
        IReadOnlyList<string> SearchTitles);

    public sealed record MetadataCandidate(
        string Id,
        string Title,
        string OriginalTitle,
        int? Year,
        double Popularity,
        double VoteCount);

    public sealed record MetadataCandidateMatchScore(
        string CandidateId,
        double Score,
        double TitleScore,
        double YearScore,
        double PopularityScore,
        double VoteCountScore,
        bool IsAcceptable,
        string Reason);

    public static class MetadataTitleAnalyzer
    {
        private static readonly Regex BracketPattern = new(@"\[[^\]]*\]|\([^\)]*\)|\{[^\}]*\}", RegexOptions.Compiled);
        private static readonly Regex YearPattern = new(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
        private static readonly Regex ImdbIdPattern = new(@"\btt\d{6,10}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TmdbIdPattern = new(@"\btmdb[:\s_-]*\d{2,9}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EpisodeTokenPattern = new(
            @"\b(?:s\d{1,2}\s*e\d{1,3}|\d{1,2}x\d{1,3}|s\d{1,2}|season\s*\d{1,2}(?:\s*(?:episode|ep)\s*\d{1,3})?|sezon\s*\d{1,2}(?:\s*(?:bolum|b\p{L}l\p{L}m|episode|ep)\s*\d{1,3})?|episode\s*\d{1,3}|ep\s*\d{1,3}|bolum\s*\d{1,3}|b\p{L}l\p{L}m\s*\d{1,3})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex NoiseTokenPattern = new(
            @"\b(?:TR|TURK|TURKIYE|YERLI|YERLI\s+FILM|DUAL|HD|UHD|SD|FHD|4K|8K|480P|576P|720P|1080P|2160P|HDR|HDR10|DV|DOLBY|HEVC|H264|H265|X264|X265|AAC|AC3|DTS|DDP|ATMOS|MULTI|DUBBED|SUBBED|DUBLAJ|ALTYAZI|WEB[-\s]*DL|WEB[-\s]*RIP|WEBDL|WEBRIP|BLU[-\s]*RAY|BLURAY|BRRIP|BDRIP|HDTV|DVDRIP|NF|AMZN|DSNP|MAX|FRAGMAN|TRAILER|TEASER|CLIP|RECAP|PREVIEW|FEED|CDN|VOD|IPTV|NETFLIX|AMAZON|DISNEY|HBO|EXXEN|GAIN|PUHU|TABII|TV\+)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex ProviderPrefixPattern = new(
            @"^\s*(?:TR|TURK|TURKIYE|YERLI|DUAL|HD|UHD|4K|VOD|MOVIE|FILM|DIZI|SERIES)\s*[:|\-._/]+\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex NonFeaturePattern = new(
            @"\b(?:trailer|fragman|teaser|clip|recap|preview|behind\s+the\s+scenes|kamera\s+arkasi|feed|cdn|sample)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex TokenRegex = new(@"[a-z0-9]+", RegexOptions.Compiled);

        public static MetadataTitleAnalysis AnalyzeMovie(string rawTitle, int? providerYear = null)
        {
            return Analyze(rawTitle, providerYear, MetadataMediaKind.Movie);
        }

        public static MetadataTitleAnalysis AnalyzeSeries(string rawTitle, int? providerYear = null)
        {
            return Analyze(rawTitle, providerYear, MetadataMediaKind.Series);
        }

        public static MetadataTitleAnalysis Analyze(string rawTitle, int? providerYear, MetadataMediaKind mediaKind)
        {
            var raw = rawTitle ?? string.Empty;
            var year = providerYear ?? ExtractYear(raw);
            var marker = TryParseSeasonEpisode(raw);
            var cleanTitle = CleanProviderTitle(raw, mediaKind);
            var normalizedTitle = NormalizeTitle(cleanTitle);
            var searchTitles = BuildSearchTitles(cleanTitle, raw)
                .Where(title => title.Length >= 2 && !LooksLikeNonFeature(title))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new MetadataTitleAnalysis(
                raw,
                cleanTitle,
                normalizedTitle,
                year,
                marker.SeasonNumber,
                marker.EpisodeNumber,
                marker.SeasonNumber.HasValue || marker.EpisodeNumber.HasValue,
                searchTitles);
        }

        public static int? ExtractYear(string value)
        {
            foreach (Match match in YearPattern.Matches(value ?? string.Empty))
            {
                if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) &&
                    year >= 1900 &&
                    year <= DateTime.UtcNow.Year + 2)
                {
                    return year;
                }
            }

            return null;
        }

        public static (int? SeasonNumber, int? EpisodeNumber) TryParseSeasonEpisode(string value)
        {
            var comparable = RemoveDiacritics(value ?? string.Empty).ToLowerInvariant();
            foreach (var pattern in new[]
                     {
                         @"\bs(?<season>\d{1,2})\s*e(?<episode>\d{1,3})\b",
                         @"\b(?<season>\d{1,2})x(?<episode>\d{1,3})\b",
                         @"\bseason\s*(?<season>\d{1,2})(?:\s*(?:episode|ep)\s*(?<episode>\d{1,3}))?\b",
                         @"\bsezon\s*(?<season>\d{1,2})(?:\s*(?:bolum|episode|ep)\s*(?<episode>\d{1,3}))?\b",
                         @"\b(?:episode|ep|bolum)\s*(?<episode>\d{1,3})\b"
                     })
            {
                var match = Regex.Match(comparable, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }

                var season = TryParsePositiveInt(match.Groups["season"].Value);
                var episode = TryParsePositiveInt(match.Groups["episode"].Value);
                return (season, episode);
            }

            return (null, null);
        }

        public static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lower = RemoveDiacritics(value).ToLowerInvariant();
            var tokens = TokenRegex.Matches(lower)
                .Select(match => match.Value)
                .Where(token => token.Length > 0);

            return string.Join(" ", tokens);
        }

        public static bool LooksLikeNonFeature(string title)
        {
            return string.IsNullOrWhiteSpace(title) ||
                   NonFeaturePattern.IsMatch(RemoveDiacritics(title));
        }

        public static MetadataCandidateMatchScore ScoreCandidate(
            string queryTitle,
            int? queryYear,
            MetadataCandidate candidate,
            MetadataMediaKind mediaKind)
        {
            var normalizedQuery = NormalizeForCompactCompare(queryTitle);
            var normalizedTitle = NormalizeForCompactCompare(candidate.Title);
            var normalizedOriginalTitle = NormalizeForCompactCompare(candidate.OriginalTitle);
            var titleScore = Math.Max(
                CalculateTitleScore(normalizedQuery, normalizedTitle),
                CalculateTitleScore(normalizedQuery, normalizedOriginalTitle));

            if (LooksLikeNonFeature(candidate.Title) || LooksLikeNonFeature(candidate.OriginalTitle))
            {
                return new MetadataCandidateMatchScore(candidate.Id, 0, 0, 0, 0, 0, false, "non-feature");
            }

            if (titleScore < 70)
            {
                return new MetadataCandidateMatchScore(candidate.Id, titleScore, titleScore, 0, 0, 0, false, "weak-title");
            }

            var yearScore = 0d;
            var hasBadYearMismatch = false;
            if (queryYear.HasValue && candidate.Year.HasValue)
            {
                var delta = Math.Abs(candidate.Year.Value - queryYear.Value);
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
                    yearScore = mediaKind == MetadataMediaKind.Movie ? -26 : -14;
                    hasBadYearMismatch = true;
                }
            }

            var popularityScore = Math.Min(candidate.Popularity / 12, 8);
            var voteCountScore = Math.Min(candidate.VoteCount / 1000, 4);
            var score = titleScore + yearScore + popularityScore + voteCountScore;
            var threshold = queryYear.HasValue ? 82 : 88;
            var isAcceptable = score >= threshold;
            var reason = "accepted";

            if (hasBadYearMismatch && titleScore < 96)
            {
                isAcceptable = false;
                reason = "year-mismatch";
            }
            else if (!queryYear.HasValue && titleScore < 84)
            {
                isAcceptable = false;
                reason = "weak-unqualified-title";
            }
            else if (!isAcceptable)
            {
                reason = "below-threshold";
            }

            return new MetadataCandidateMatchScore(
                candidate.Id,
                score,
                titleScore,
                yearScore,
                popularityScore,
                voteCountScore,
                isAcceptable,
                reason);
        }

        public static MetadataCandidate? PickBestCandidate(
            string queryTitle,
            int? queryYear,
            IEnumerable<MetadataCandidate> candidates,
            MetadataMediaKind mediaKind)
        {
            MetadataCandidate? best = null;
            var bestScore = double.MinValue;
            foreach (var candidate in candidates)
            {
                var score = ScoreCandidate(queryTitle, queryYear, candidate, mediaKind);
                if (!score.IsAcceptable || score.Score <= bestScore)
                {
                    continue;
                }

                best = candidate;
                bestScore = score.Score;
            }

            return best;
        }

        private static string CleanProviderTitle(string rawTitle, MetadataMediaKind mediaKind)
        {
            var title = RemoveDiacritics(rawTitle ?? string.Empty).Replace('&', ' ');
            title = ImdbIdPattern.Replace(title, " ");
            title = TmdbIdPattern.Replace(title, " ");
            title = Regex.Replace(title, @"https?://\S+", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            title = NormalizeDecorativeSeparators(title);
            title = BracketPattern.Replace(title, " ");
            title = EpisodeTokenPattern.Replace(title, " ");
            title = YearPattern.Replace(title, " ");

            for (var i = 0; i < 4; i++)
            {
                title = ProviderPrefixPattern.Replace(title, " ");
            }

            title = NoiseTokenPattern.Replace(title, " ");
            title = Regex.Replace(title, @"\b\d{1,3}\s*(?:fps|bit|bitrate|hz)\b", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            title = Regex.Replace(title, @"[_|\u2022\u00B7]+", " ");
            title = Regex.Replace(title, @"\s*[-:]+\s*", " ");
            title = Regex.Replace(title, @"[^\p{L}\p{Nd}' ]+", " ");
            title = Regex.Replace(title, @"\s+", " ");

            var cleaned = title.Trim(' ', '-', ':', '.', '_', '|', '/', '\\', '\'');
            if (mediaKind == MetadataMediaKind.Series)
            {
                cleaned = Regex.Replace(cleaned, @"\bseason\s*$|\bsezon\s*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            }

            return cleaned;
        }

        private static IEnumerable<string> BuildSearchTitles(string cleanTitle, string rawTitle)
        {
            if (!string.IsNullOrWhiteSpace(cleanTitle))
            {
                yield return NormalizeSpacing(cleanTitle);
            }

            foreach (var part in SplitTitleParts(cleanTitle))
            {
                yield return NormalizeSpacing(part);
            }

            var rawWithoutYear = YearPattern.Replace(RemoveDiacritics(rawTitle ?? string.Empty), " ");
            foreach (var part in SplitTitleParts(rawWithoutYear))
            {
                var cleaned = CleanProviderTitle(part, MetadataMediaKind.Movie);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    yield return NormalizeSpacing(cleaned);
                }
            }
        }

        private static IEnumerable<string> SplitTitleParts(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                yield break;
            }

            foreach (var part in Regex.Split(title, @"\s+(?:/|\||-|\:)\s+|(?:/|\||-|\:)"))
            {
                var normalized = NormalizeSpacing(part);
                if (normalized.Length >= 2)
                {
                    yield return normalized;
                }
            }
        }

        private static string NormalizeDecorativeSeparators(string value)
        {
            var title = Regex.Replace(value ?? string.Empty, @"(?<=\p{L}|\d)[._]+(?=\p{L}|\d)", " ");
            return Regex.Replace(title, @"[._]{2,}", " ");
        }

        private static string NormalizeSpacing(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        }

        private static int? TryParsePositiveInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0
                ? number
                : null;
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
            return TokenRegex.Matches(value)
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

        private static string NormalizeForCompactCompare(string value)
        {
            return Regex.Replace(RemoveDiacritics(value).ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
        }

        private static string RemoveDiacritics(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC)
                .Replace('\u0131', 'i')
                .Replace('\u0130', 'I');
        }
    }
}

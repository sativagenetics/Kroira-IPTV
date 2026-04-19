using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kroira.App.Models;

namespace Kroira.App.Services.Parsing
{
    public interface ICatalogNormalizationService
    {
        CatalogNormalizationResult NormalizeMovie(SourceType sourceType, string rawTitle, string rawCategoryName);
        CatalogNormalizationResult NormalizeSeries(SourceType sourceType, string rawTitle, string rawCategoryName);
    }

    public sealed class CatalogNormalizationResult
    {
        public string Title { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public string RawTitle { get; init; } = string.Empty;
        public string RawCategoryName { get; init; } = string.Empty;
        public string ContentKind { get; init; } = "Primary";
    }

    public sealed class CatalogNormalizationService : ICatalogNormalizationService
    {
        private static readonly Regex SegmentSplitRegex =
            new(@"\s+(?:\||-|–|—)\s+", RegexOptions.Compiled);

        private static readonly Regex MultiWhitespaceRegex =
            new(@"\s{2,}", RegexOptions.Compiled);

        private static readonly Regex TrailingTagRegex =
            new(@"\s*[\[\(\{](?<tag>[^\]\)\}]+)[\]\)\}]\s*$", RegexOptions.Compiled);

        private static readonly Regex LeadingTagRegex =
            new(@"^\s*[\[\(\{](?<tag>[^\]\)\}]+)[\]\)\}]\s*", RegexOptions.Compiled);

        private static readonly Regex LeadingCodePrefixRegex =
            new(@"^(?<tag>[A-Za-z]{2,4})\s*[:|]\s*", RegexOptions.Compiled);

        private static readonly Regex TrailingBoundaryTokenRegex =
            new(@"\s+(?<tag>(?:official|final|extended|exclusive)\s+)?(?:trailer|teaser|preview|clip)s?\s*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TrailingNoiseRegex =
            new(@"\s+(?:2160p|1080p|720p|480p|4k|uhd|fhd|hd|sd|hdr10|hdr|hevc|x264|x265|h264|h265|web(?:-|\s)?dl|webrip|bluray|bdrip|dvdrip|hdtv|multi(?:\s+audio)?|dual\s+audio|subbed|dubbed)\b\s*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> QualityTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "4k", "2160p", "1080p", "720p", "480p", "uhd", "fhd", "hd", "sd",
            "hdr", "hdr10", "hevc", "x264", "x265", "h264", "h265",
            "webdl", "web-dl", "webrip", "bluray", "bdrip", "dvdrip", "hdtv"
        };

        private static readonly HashSet<string> LanguageTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "tr", "tur", "turkish", "en", "eng", "english", "de", "ger", "german",
            "fr", "fre", "french", "es", "spa", "spanish", "it", "ita", "italian",
            "pt", "por", "portuguese", "ar", "ara", "arabic", "ru", "rus", "russian",
            "multi", "multi audio", "multiaudio", "dual audio", "dualaudio", "subbed", "dubbed"
        };

        private static readonly HashSet<string> ProviderTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "provider", "provider vod", "vod", "vod library", "vod movies", "vod series",
            "playlist", "package", "reseller", "trial", "server", "backup", "new source"
        };

        private static readonly HashSet<string> MovieCategoryTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "movie", "movies", "film", "films", "cinema"
        };

        private static readonly HashSet<string> SeriesCategoryTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "series", "tv show", "tv shows", "show", "shows"
        };

        public CatalogNormalizationResult NormalizeMovie(SourceType sourceType, string rawTitle, string rawCategoryName)
        {
            return Normalize(sourceType, rawTitle, rawCategoryName, "Movies", MovieCategoryTokens);
        }

        public CatalogNormalizationResult NormalizeSeries(SourceType sourceType, string rawTitle, string rawCategoryName)
        {
            return Normalize(sourceType, rawTitle, rawCategoryName, "Series", SeriesCategoryTokens);
        }

        private static CatalogNormalizationResult Normalize(
            SourceType sourceType,
            string rawTitle,
            string rawCategoryName,
            string defaultCategory,
            HashSet<string> typeCategoryTokens)
        {
            var trimmedRawTitle = rawTitle?.Trim() ?? string.Empty;
            var trimmedRawCategory = rawCategoryName?.Trim() ?? string.Empty;

            var contentKind = DetectContentKind(trimmedRawTitle);
            var normalizedTitle = NormalizeTitle(trimmedRawTitle, contentKind, sourceType);
            var normalizedCategory = NormalizeCategory(trimmedRawCategory, defaultCategory, typeCategoryTokens);

            return new CatalogNormalizationResult
            {
                Title = string.IsNullOrWhiteSpace(normalizedTitle) ? FallbackTitle(trimmedRawTitle, defaultCategory) : normalizedTitle,
                CategoryName = normalizedCategory,
                RawTitle = trimmedRawTitle,
                RawCategoryName = trimmedRawCategory,
                ContentKind = contentKind
            };
        }

        private static string NormalizeTitle(string rawTitle, string contentKind, SourceType sourceType)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                return string.Empty;
            }

            var working = rawTitle.Trim();
            working = StripLeadingWrappedTags(working, tag => IsIgnorableTitleTag(tag, contentKind));
            working = StripTrailingWrappedTags(working, tag => IsIgnorableTitleTag(tag, contentKind));
            working = StripLeadingCodePrefix(working);

            var segments = SplitSegments(working);
            TrimBoundarySegments(segments, segment => IsIgnorableTitleSegment(segment, contentKind));

            working = segments.Count > 0 ? string.Join(" - ", segments) : working;
            working = TrailingBoundaryTokenRegex.Replace(working, string.Empty);
            working = StripTrailingNoise(working);
            working = CleanupText(working);

            if (sourceType == SourceType.Xtream && working.Length < 2)
            {
                return CleanupText(rawTitle);
            }

            return working;
        }

        private static string NormalizeCategory(string rawCategory, string defaultCategory, HashSet<string> typeCategoryTokens)
        {
            if (string.IsNullOrWhiteSpace(rawCategory))
            {
                return "Uncategorized";
            }

            var working = rawCategory.Trim();
            working = StripLeadingWrappedTags(working, IsIgnorableCategoryTag);
            working = StripTrailingWrappedTags(working, IsIgnorableCategoryTag);
            working = StripLeadingCodePrefix(working);

            var segments = SplitSegments(working);
            TrimBoundarySegments(segments, IsIgnorableCategorySegment);
            working = segments.Count > 0 ? string.Join(" / ", segments) : working;
            working = StripBoundaryCategoryWords(working, typeCategoryTokens);
            working = StripTrailingNoise(working);
            working = CleanupText(working);

            if (string.IsNullOrWhiteSpace(working))
            {
                return LooksLikeGenericCategory(rawCategory, typeCategoryTokens) ? defaultCategory : "Uncategorized";
            }

            return working;
        }

        private static string StripBoundaryCategoryWords(string value, HashSet<string> typeCategoryTokens)
        {
            var working = value;

            while (true)
            {
                var previous = working;
                working = StripLeadingCategoryWord(working, typeCategoryTokens);
                working = StripTrailingCategoryWord(working, typeCategoryTokens);

                if (string.Equals(previous, working, StringComparison.Ordinal))
                {
                    return working;
                }
            }
        }

        private static string StripLeadingCategoryWord(string value, HashSet<string> typeCategoryTokens)
        {
            foreach (var token in typeCategoryTokens.Concat(ProviderTokens))
            {
                if (value.StartsWith(token + " ", StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring(token.Length).TrimStart(' ', '/', '-', '|');
                }
            }

            return value;
        }

        private static string StripTrailingCategoryWord(string value, HashSet<string> typeCategoryTokens)
        {
            foreach (var token in typeCategoryTokens.Concat(ProviderTokens))
            {
                if (value.EndsWith(" " + token, StringComparison.OrdinalIgnoreCase))
                {
                    return value[..^token.Length].TrimEnd(' ', '/', '-', '|');
                }
            }

            return value;
        }

        private static bool LooksLikeGenericCategory(string rawCategory, HashSet<string> typeCategoryTokens)
        {
            var normalized = NormalizeToken(rawCategory);
            if (ProviderTokens.Contains(normalized) || typeCategoryTokens.Contains(normalized))
            {
                return true;
            }

            var stripped = normalized;
            foreach (var token in typeCategoryTokens
                         .Concat(ProviderTokens)
                         .Concat(QualityTokens)
                         .Concat(LanguageTokens)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                stripped = Regex.Replace(
                    stripped,
                    $@"\b{Regex.Escape(token)}\b",
                    string.Empty,
                    RegexOptions.IgnoreCase);
            }

            return string.IsNullOrWhiteSpace(CleanupText(stripped));
        }

        private static string DetectContentKind(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                return "Primary";
            }

            var segments = SplitSegments(rawTitle);
            foreach (var segment in segments)
            {
                var kind = DetectKindFromToken(segment);
                if (!string.Equals(kind, "Primary", StringComparison.Ordinal))
                {
                    return kind;
                }
            }

            var trailingMatch = TrailingBoundaryTokenRegex.Match(rawTitle);
            if (trailingMatch.Success)
            {
                return DetectKindFromToken(trailingMatch.Groups["tag"].Value);
            }

            return "Primary";
        }

        private static string DetectKindFromToken(string value)
        {
            var token = NormalizeToken(value);
            if (string.IsNullOrWhiteSpace(token))
            {
                return "Primary";
            }

            if (token.EndsWith("trailer", StringComparison.OrdinalIgnoreCase) || token == "trailers")
            {
                return "Trailer";
            }

            if (token.EndsWith("teaser", StringComparison.OrdinalIgnoreCase) || token == "teasers")
            {
                return "Teaser";
            }

            if (token.EndsWith("preview", StringComparison.OrdinalIgnoreCase) || token == "previews")
            {
                return "Preview";
            }

            if (token.EndsWith("clip", StringComparison.OrdinalIgnoreCase) || token == "clips")
            {
                return "Clip";
            }

            return "Primary";
        }

        private static string StripLeadingWrappedTags(string value, Func<string, bool> shouldStrip)
        {
            var working = value;
            while (true)
            {
                var match = LeadingTagRegex.Match(working);
                if (!match.Success || !shouldStrip(match.Groups["tag"].Value))
                {
                    return working;
                }

                working = working[match.Length..].TrimStart();
            }
        }

        private static string StripTrailingWrappedTags(string value, Func<string, bool> shouldStrip)
        {
            var working = value;
            while (true)
            {
                var match = TrailingTagRegex.Match(working);
                if (!match.Success || !shouldStrip(match.Groups["tag"].Value))
                {
                    return working;
                }

                working = working[..match.Index].TrimEnd();
            }
        }

        private static string StripLeadingCodePrefix(string value)
        {
            var working = value;
            while (true)
            {
                var match = LeadingCodePrefixRegex.Match(working);
                if (!match.Success || !LanguageTokens.Contains(NormalizeToken(match.Groups["tag"].Value)))
                {
                    return working;
                }

                working = working[match.Length..].TrimStart();
            }
        }

        private static List<string> SplitSegments(string value)
        {
            return SegmentSplitRegex
                .Split(value)
                .Select(CleanupText)
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToList();
        }

        private static void TrimBoundarySegments(List<string> segments, Func<string, bool> shouldStrip)
        {
            while (segments.Count > 1 && shouldStrip(segments[0]))
            {
                segments.RemoveAt(0);
            }

            while (segments.Count > 1 && shouldStrip(segments[^1]))
            {
                segments.RemoveAt(segments.Count - 1);
            }
        }

        private static bool IsIgnorableTitleTag(string tag, string contentKind)
        {
            return IsIgnorableTitleSegment(tag) || IsContentKindTag(tag, contentKind);
        }

        private static bool IsIgnorableCategoryTag(string tag)
        {
            return IsIgnorableCategorySegment(tag);
        }

        private static bool IsIgnorableTitleSegment(string segment, string contentKind)
        {
            return IsIgnorableTitleSegment(segment) || IsContentKindTag(segment, contentKind);
        }

        private static bool IsIgnorableTitleSegment(string segment)
        {
            var token = NormalizeToken(segment);
            if (string.IsNullOrWhiteSpace(token))
            {
                return true;
            }

            return QualityTokens.Contains(token)
                || LanguageTokens.Contains(token)
                || ProviderTokens.Contains(token);
        }

        private static bool IsContentKindTag(string segment, string contentKind)
        {
            var detected = DetectKindFromToken(segment);
            return !string.Equals(detected, "Primary", StringComparison.Ordinal)
                && string.Equals(detected, contentKind, StringComparison.Ordinal);
        }

        private static bool IsIgnorableCategorySegment(string segment)
        {
            var token = NormalizeToken(segment);
            if (string.IsNullOrWhiteSpace(token))
            {
                return true;
            }

            return QualityTokens.Contains(token)
                || LanguageTokens.Contains(token)
                || ProviderTokens.Contains(token)
                || MovieCategoryTokens.Contains(token)
                || SeriesCategoryTokens.Contains(token);
        }

        private static string CleanupText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = value.Trim(' ', '-', '|', '/', ':', '_', '.', ',', ';');
            cleaned = MultiWhitespaceRegex.Replace(cleaned, " ");
            return cleaned.Trim();
        }

        private static string StripTrailingNoise(string value)
        {
            var working = value;
            while (true)
            {
                var stripped = TrailingNoiseRegex.Replace(working, string.Empty);
                if (string.Equals(stripped, working, StringComparison.Ordinal))
                {
                    return working;
                }

                working = stripped.TrimEnd();
            }
        }

        private static string NormalizeToken(string value)
        {
            return CleanupText(value).ToLowerInvariant();
        }

        private static string FallbackTitle(string rawTitle, string defaultCategory)
        {
            if (!string.IsNullOrWhiteSpace(rawTitle))
            {
                return CleanupText(rawTitle);
            }

            return defaultCategory == "Series" ? "Unknown Series" : "Unknown Movie";
        }
    }
}

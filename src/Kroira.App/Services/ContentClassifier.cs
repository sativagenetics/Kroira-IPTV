using System;
using System.Collections.Generic;
using System.Linq;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public static class ContentClassifier
    {
        private static readonly HashSet<string> MovieExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "mp4", "mkv", "avi", "mov", "m4v", "mpg", "mpeg", "wmv", "flv", "webm"
        };

        private static readonly HashSet<string> NonMovieExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "ts", "m3u8", "m3u", "php", "txt", "html", "htm"
        };

        public static HashSet<string> BuildCategoryLabelSet(IEnumerable<string> categoryNames)
        {
            return categoryNames
                .Select(NormalizeLabel)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsProviderCategoryRow(string title, HashSet<string> categoryLabels)
        {
            if (categoryLabels.Count == 0 || string.IsNullOrWhiteSpace(title)) return false;
            return categoryLabels.Contains(NormalizeLabel(title));
        }

        public static bool IsPlayableXtreamLiveChannel(string name, string streamUrl, HashSet<string> categoryLabels)
        {
            if (IsGarbageTitle(name)) return false;
            if (string.IsNullOrWhiteSpace(streamUrl)) return false;
            if (IsProviderCategoryRow(name, categoryLabels)) return false;

            var lowerUrl = streamUrl.Trim().ToLowerInvariant();
            return lowerUrl.Contains("/live/");
        }

        public static bool IsPlayableM3uLiveChannel(string name, string streamUrl, HashSet<string> categoryLabels)
        {
            if (IsGarbageTitle(name)) return false;
            if (string.IsNullOrWhiteSpace(streamUrl)) return false;
            if (IsProviderCategoryRow(name, categoryLabels)) return false;

            var lowerUrl = streamUrl.Trim().ToLowerInvariant();
            if (lowerUrl.Contains("/movie/") || lowerUrl.Contains("/series/") || lowerUrl.Contains("/vod/")) return false;

            var extension = GetUrlExtension(lowerUrl);
            return string.IsNullOrEmpty(extension) || !MovieExtensions.Contains(extension);
        }

        public static bool IsPlayableStoredLiveChannel(string name, string streamUrl, SourceType sourceType, HashSet<string> categoryLabels)
        {
            return sourceType == SourceType.Xtream
                ? IsPlayableXtreamLiveChannel(name, streamUrl, categoryLabels)
                : IsPlayableM3uLiveChannel(name, streamUrl, categoryLabels);
        }

        public static bool IsPlayableXtreamMovie(string title, string streamUrl, HashSet<string> categoryLabels)
        {
            if (IsGarbageTitle(title)) return false;
            if (string.IsNullOrWhiteSpace(streamUrl)) return false;
            if (IsProviderCategoryRow(title, categoryLabels)) return false;

            var lowerUrl = streamUrl.Trim().ToLowerInvariant();
            return lowerUrl.Contains("/movie/");
        }

        public static bool IsBrowsableXtreamSeries(string title, HashSet<string> categoryLabels)
        {
            if (IsGarbageTitle(title)) return false;
            return !IsProviderCategoryRow(title, categoryLabels);
        }

        public static bool IsGarbageCategoryName(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return false;
            var lower = categoryName.Trim().ToLowerInvariant();
            return lower.Contains("playlist") ||
                   lower.Contains("package") ||
                   lower.Contains(" trial") ||
                   lower.Contains("trial ") ||
                   lower == "trial" ||
                   lower.Contains("| test") ||
                   lower.Contains("test |") ||
                   lower == "test" ||
                   lower == "demo" ||
                   lower.Contains("xxx pack") ||
                   lower.Contains("xxx-pack") ||
                   lower.Contains("xxx_pack") ||
                   lower.Contains("iptv pack") ||
                   lower.Contains("reseller") ||
                   lower.Contains("credits") ||
                   lower.Contains("placeholder");
        }

        public static bool IsGarbageTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return true;
            var lower = title.Trim().ToLowerInvariant();
            if (lower.Length <= 2) return true;

            return lower == "unknown" ||
                   lower == "unknown movie" ||
                   lower == "unknown series" ||
                   lower == "unknown channel" ||
                   lower.Contains("[placeholder]") ||
                   lower.Contains("[test]") ||
                   lower.Contains("[demo]") ||
                   lower.StartsWith("test channel") ||
                   lower.StartsWith("test stream");
        }

        public static bool IsGarbageMovieExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return false;
            return NonMovieExtensions.Contains(extension.Trim().TrimStart('.'));
        }

        public static string NormalizeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return string.Join(" ", value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string GetUrlExtension(string streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl)) return string.Empty;

            var withoutQuery = streamUrl.Split('?')[0].TrimEnd('/');
            var dotIndex = withoutQuery.LastIndexOf('.');
            if (dotIndex < 0 || dotIndex == withoutQuery.Length - 1) return string.Empty;

            var slashIndex = withoutQuery.LastIndexOf('/');
            if (slashIndex > dotIndex) return string.Empty;

            return withoutQuery[(dotIndex + 1)..].ToLowerInvariant();
        }
    }
}

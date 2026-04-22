#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kroira.App.Services
{
    public sealed record StalkerStreamLocator(
        int SourceProfileId,
        string ResourceType,
        string ItemId,
        string Command,
        string? SeriesId = null,
        int SeasonNumber = 0,
        int EpisodeNumber = 0);

    internal static class StalkerLocatorCodec
    {
        public static string Encode(StalkerStreamLocator locator)
        {
            var resourceType = NormalizeSegment(locator.ResourceType, "live");
            var itemId = Uri.EscapeDataString((locator.ItemId ?? string.Empty).Trim());
            var queryParts = new List<string>
            {
                $"source={locator.SourceProfileId}",
                $"cmd={Uri.EscapeDataString(locator.Command ?? string.Empty)}"
            };

            if (!string.IsNullOrWhiteSpace(locator.SeriesId))
            {
                queryParts.Add($"series={Uri.EscapeDataString(locator.SeriesId.Trim())}");
            }

            if (locator.SeasonNumber > 0)
            {
                queryParts.Add($"season={locator.SeasonNumber}");
            }

            if (locator.EpisodeNumber > 0)
            {
                queryParts.Add($"episode={locator.EpisodeNumber}");
            }

            return $"stalker://{resourceType}/{itemId}?{string.Join("&", queryParts)}";
        }

        public static bool TryParse(string? value, out StalkerStreamLocator locator)
        {
            locator = new StalkerStreamLocator(0, string.Empty, string.Empty, string.Empty);
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, "stalker", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var query = ParseQuery(uri.Query);
            var itemId = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
            var resourceType = NormalizeSegment(uri.Host, string.Empty);
            var command = query.TryGetValue("cmd", out var cmd)
                ? Uri.UnescapeDataString(cmd)
                : string.Empty;

            if (!int.TryParse(query.GetValueOrDefault("source"), out var sourceProfileId) ||
                sourceProfileId <= 0 ||
                string.IsNullOrWhiteSpace(resourceType) ||
                string.IsNullOrWhiteSpace(itemId) ||
                string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            locator = new StalkerStreamLocator(
                sourceProfileId,
                resourceType,
                itemId,
                command,
                query.TryGetValue("series", out var seriesId) ? Uri.UnescapeDataString(seriesId) : null,
                ParseInt(query.GetValueOrDefault("season")),
                ParseInt(query.GetValueOrDefault("episode")));
            return true;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var splitIndex = part.IndexOf('=');
                if (splitIndex <= 0)
                {
                    continue;
                }

                var key = part[..splitIndex];
                var value = part[(splitIndex + 1)..];
                result[key] = value;
            }

            return result;
        }

        private static int ParseInt(string? value)
        {
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }

        private static string NormalizeSegment(string? value, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? fallback
                : string.Concat(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '_'));
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }
    }
}

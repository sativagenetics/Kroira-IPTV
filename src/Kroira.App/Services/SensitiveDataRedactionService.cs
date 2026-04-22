#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kroira.App.Services
{
    public interface ISensitiveDataRedactionService
    {
        string RedactUrl(string? value);
        string RedactLooseText(string? value);
        string RedactMacAddress(string? value);
        string RedactSecret(string? value);
    }

    public sealed class SensitiveDataRedactionService : ISensitiveDataRedactionService
    {
        private static readonly Regex AbsoluteUrlRegex = new(
            @"\b[a-z][a-z0-9+\-.]*://[^\s""'<>]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex MacAddressRegex = new(
            @"\b[0-9a-f]{2}(?:[:-][0-9a-f]{2}){5}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex SensitivePairRegex = new(
            @"(?i)\b((?:username|user|password|pass|token|play_token|mac|serial|device(?:_?id)?|signature|sig|cookie|auth(?:orization)?)|(?:[a-z0-9_:-]*token[a-z0-9_:-]*)|(?:[a-z0-9_:-]*secret[a-z0-9_:-]*))\s*=\s*([^\s&;]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "username",
            "user",
            "password",
            "pass",
            "token",
            "auth",
            "authorization",
            "cookie",
            "mac",
            "serial",
            "device",
            "device_id",
            "deviceid",
            "signature",
            "sig",
            "play_token",
            "cmd",
            "upstream"
        };

        public string RedactUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (StalkerLocatorCodec.TryParse(trimmed, out var locator))
            {
                var queryParts = new List<string>
                {
                    $"source={locator.SourceProfileId}",
                    "cmd=%2A%2A%2A"
                };

                if (!string.IsNullOrWhiteSpace(locator.SeriesId))
                {
                    queryParts.Add($"series={Uri.EscapeDataString(locator.SeriesId)}");
                }

                if (locator.SeasonNumber > 0)
                {
                    queryParts.Add($"season={locator.SeasonNumber}");
                }

                if (locator.EpisodeNumber > 0)
                {
                    queryParts.Add($"episode={locator.EpisodeNumber}");
                }

                return $"stalker://{locator.ResourceType}/{Uri.EscapeDataString(locator.ItemId)}?{string.Join("&", queryParts)}";
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return RedactLooseText(trimmed);
            }

            var builder = new UriBuilder(uri);
            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                builder.UserName = "***";
                builder.Password = "***";
            }

            var pathSegments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString)
                .ToList();

            var credentialSegmentIndex = pathSegments.FindIndex(IsCredentialPath);
            if (credentialSegmentIndex >= 0 && pathSegments.Count >= credentialSegmentIndex + 3)
            {
                pathSegments[credentialSegmentIndex + 1] = "***";
                pathSegments[credentialSegmentIndex + 2] = "***";
            }

            builder.Path = pathSegments.Count == 0
                ? uri.AbsolutePath
                : "/" + string.Join("/", pathSegments.Select(Uri.EscapeDataString));
            builder.Query = BuildRedactedQuery(uri.Query);
            return builder.Uri.AbsoluteUri;
        }

        public string RedactLooseText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var result = value.Trim();
            result = AbsoluteUrlRegex.Replace(result, match =>
            {
                var token = match.Value;
                var suffix = string.Empty;
                while (token.Length > 0 && IsTrailingPunctuation(token[^1]))
                {
                    suffix = token[^1] + suffix;
                    token = token[..^1];
                }

                return RedactUrl(token) + suffix;
            });

            result = MacAddressRegex.Replace(result, match => RedactMacAddress(match.Value));
            result = SensitivePairRegex.Replace(result, match => $"{match.Groups[1].Value}=***");
            return result;
        }

        public string RedactMacAddress(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var hex = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            if (hex.Length < 12)
            {
                return "***";
            }

            return $"{hex[..2]}:{hex[2..4]}:**:**:**:{hex[^2..]}";
        }

        public string RedactSecret(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : "Configured (redacted)";
        }

        private string BuildRedactedQuery(string query)
        {
            var trimmed = (query ?? string.Empty).TrimStart('?');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = segment.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    parts.Add(segment);
                    continue;
                }

                var rawKey = segment[..separatorIndex];
                var rawValue = segment[(separatorIndex + 1)..];
                var key = Uri.UnescapeDataString(rawKey);
                var value = Uri.UnescapeDataString(rawValue);
                var redactedValue = IsSensitiveQueryKey(key)
                    ? "***"
                    : value.Contains("://", StringComparison.OrdinalIgnoreCase)
                        ? RedactUrl(value)
                        : LooksOpaqueSecret(value)
                            ? MaskMiddle(value, 4, 2)
                            : value;

                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(redactedValue)}");
            }

            return string.Join("&", parts);
        }

        private static bool IsCredentialPath(string firstSegment)
        {
            return string.Equals(firstSegment, "live", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(firstSegment, "movie", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(firstSegment, "series", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(firstSegment, "timeshift", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSensitiveQueryKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var trimmed = key.Trim();
            if (SensitiveQueryKeys.Contains(trimmed))
            {
                return true;
            }

            return trimmed.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "auth", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "sig", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "mac", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "serial", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "device", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "device_id", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "deviceid", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksOpaqueSecret(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 24)
            {
                return false;
            }

            return value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '=' or '+' or '/');
        }

        private static string MaskMiddle(string value, int prefixLength, int suffixLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= prefixLength + suffixLength + 4)
            {
                return "***";
            }

            return $"{value[..prefixLength]}***{value[^suffixLength..]}";
        }

        private static bool IsTrailingPunctuation(char value)
        {
            return value is '.' or ',' or ';' or ')' or ']';
        }
    }
}

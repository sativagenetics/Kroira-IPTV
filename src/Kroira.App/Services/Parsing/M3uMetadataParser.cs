using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kroira.App.Services.Parsing
{
    internal sealed class M3uHeaderMetadata
    {
        public IReadOnlyList<string> HeaderLines { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes { get; init; }
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<string> XmltvUrls { get; init; } = Array.Empty<string>();
        public string RawHeaderPreview { get; init; } = string.Empty;
    }

    internal sealed class M3uExtinfMetadata
    {
        public string DisplayName { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string> Attributes { get; init; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class M3uMetadataParser
    {
        private static readonly Regex AttributeRegex = new(
            @"(?<key>[A-Za-z0-9_-]+)\s*=\s*(?:(?<quote>['""])(?<quoted>.*?)\k<quote>|(?<bare>[^\s]+))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AbsoluteUrlRegex = new(
            @"[a-z][a-z0-9+\-.]*://[^\s,;|]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] XmltvAttributeNames =
        {
            "x-tvg-url",
            "url-tvg",
            "tvg-url",
            "epg-url",
            "xmltv"
        };

        public static M3uHeaderMetadata ParseHeaderMetadata(string playlistContent, string playlistLocation)
        {
            var headerLines = new List<string>();
            foreach (var rawLine in playlistContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase) || !line.StartsWith("#", StringComparison.Ordinal))
                {
                    break;
                }

                headerLines.Add(line);
            }

            var attributes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var headerLine in headerLines)
            {
                foreach (Match match in AttributeRegex.Matches(headerLine))
                {
                    var key = match.Groups["key"].Value.Trim();
                    var value = match.Groups["quoted"].Success
                        ? match.Groups["quoted"].Value
                        : match.Groups["bare"].Value;

                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (!attributes.TryGetValue(key, out var values))
                    {
                        values = new List<string>();
                        attributes[key] = values;
                    }

                    values.Add(value.Trim());
                }
            }

            var xmltvUrls = new List<string>();
            foreach (var attrName in XmltvAttributeNames)
            {
                if (!attributes.TryGetValue(attrName, out var values))
                {
                    continue;
                }

                foreach (var value in values)
                {
                    foreach (var candidate in SplitPossibleUrls(value))
                    {
                        var resolved = ResolveUrl(candidate, playlistLocation);
                        if (!string.IsNullOrWhiteSpace(resolved))
                        {
                            xmltvUrls.Add(resolved);
                        }
                    }
                }
            }

            var distinctUrls = xmltvUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new M3uHeaderMetadata
            {
                HeaderLines = headerLines,
                Attributes = attributes.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase),
                XmltvUrls = distinctUrls,
                RawHeaderPreview = string.Join(" | ", headerLines.Take(4))
            };
        }

        public static M3uExtinfMetadata ParseExtinf(string line)
        {
            var separatorIndex = FindMetadataSeparator(line);
            var metadataSegment = separatorIndex >= 0
                ? line[..separatorIndex]
                : line;
            var displayName = separatorIndex >= 0 && separatorIndex < line.Length - 1
                ? line[(separatorIndex + 1)..].Trim()
                : string.Empty;

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in AttributeRegex.Matches(metadataSegment))
            {
                var key = match.Groups["key"].Value.Trim();
                var value = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["bare"].Value;

                if (!string.IsNullOrWhiteSpace(key))
                {
                    attributes[key] = value.Trim();
                }
            }

            return new M3uExtinfMetadata
            {
                DisplayName = displayName,
                Attributes = attributes
            };
        }

        public static string GetFirstAttributeValue(
            IReadOnlyDictionary<string, string> attributes,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                if (attributes.TryGetValue(key, out var value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        public static string ResolveUrl(string value, string playlistLocation)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                return value;
            }

            if (Uri.TryCreate(playlistLocation, UriKind.Absolute, out var playlistUri))
            {
                return new Uri(playlistUri, value).ToString();
            }

            var playlistDirectory = Path.GetDirectoryName(playlistLocation);
            if (!string.IsNullOrWhiteSpace(playlistDirectory))
            {
                return Path.GetFullPath(Path.Combine(playlistDirectory, value));
            }

            return value;
        }

        public static string TryBuildXtreamXmltvUrl(string playlistLocation)
        {
            if (!Uri.TryCreate(playlistLocation, UriKind.Absolute, out var playlistUri))
            {
                return string.Empty;
            }

            var fileName = Path.GetFileName(playlistUri.AbsolutePath);
            if (!string.Equals(fileName, "get.php", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var query = playlistUri.Query.TrimStart('?');
            if (!ContainsQueryParameter(query, "username") || !ContainsQueryParameter(query, "password"))
            {
                return string.Empty;
            }

            var builder = new UriBuilder(playlistUri)
            {
                Path = ReplaceFileName(playlistUri.AbsolutePath, "xmltv.php"),
                Query = query
            };

            return builder.Uri.ToString();
        }

        private static IReadOnlyList<string> SplitPossibleUrls(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            var trimmed = value.Trim();
            var absoluteUrls = AbsoluteUrlRegex
                .Matches(trimmed)
                .Select(match => match.Value.Trim())
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .ToList();
            if (absoluteUrls.Count > 1)
            {
                return absoluteUrls;
            }

            if (trimmed.IndexOfAny(new[] { ',', ';', '|' }) < 0)
            {
                return new[] { trimmed };
            }

            return trimmed
                .Split(new[] { ',', ';', '|'}, StringSplitOptions.RemoveEmptyEntries)
                .Select(candidate => candidate.Trim())
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .ToList();
        }

        private static int FindMetadataSeparator(string line)
        {
            var quote = '\0';
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (quote == '\0')
                {
                    if (ch is '"' or '\'')
                    {
                        quote = ch;
                        continue;
                    }

                    if (ch == ',')
                    {
                        return i;
                    }
                }
                else if (ch == quote)
                {
                    quote = '\0';
                }
            }

            return -1;
        }

        private static bool ContainsQueryParameter(string query, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                var name = separator >= 0 ? part[..separator] : part;
                if (string.Equals(name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReplaceFileName(string absolutePath, string newFileName)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return newFileName;
            }

            var lastSlashIndex = absolutePath.LastIndexOf('/');
            if (lastSlashIndex < 0)
            {
                return newFileName;
            }

            return absolutePath[..(lastSlashIndex + 1)] + newFileName;
        }
    }
}

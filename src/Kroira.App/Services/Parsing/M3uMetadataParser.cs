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
                var line = StripBom(rawLine).Trim();
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
                foreach (var token in ParseAttributeTokens(headerLine))
                {
                    if (string.IsNullOrWhiteSpace(token.Key) || string.IsNullOrWhiteSpace(token.Value))
                    {
                        continue;
                    }

                    if (!attributes.TryGetValue(token.Key, out var values))
                    {
                        values = new List<string>();
                        attributes[token.Key] = values;
                    }

                    values.Add(token.Value.Trim());
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
            var normalizedLine = StripBom(line).Trim();
            var separatorIndex = FindMetadataSeparator(normalizedLine);
            var metadataSegment = separatorIndex >= 0
                ? normalizedLine[..separatorIndex]
                : normalizedLine;
            var displayName = separatorIndex >= 0 && separatorIndex < normalizedLine.Length - 1
                ? normalizedLine[(separatorIndex + 1)..].Trim()
                : string.Empty;

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tokens = ParseAttributeTokens(metadataSegment);
            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token.Key))
                {
                    attributes[token.Key] = token.Value.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = InferDisplayNameWithoutComma(normalizedLine, tokens);
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
                else if (ch == '\\' && i + 1 < line.Length)
                {
                    i++;
                }
                else if (ch == quote && i + 1 < line.Length && line[i + 1] == quote)
                {
                    i++;
                }
                else if (ch == quote)
                {
                    quote = '\0';
                }
            }

            return -1;
        }

        private static IReadOnlyList<AttributeToken> ParseAttributeTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<AttributeToken>();
            }

            var tokens = new List<AttributeToken>();
            var index = 0;
            while (index < text.Length)
            {
                if (!IsAttributeKeyChar(text[index]))
                {
                    index++;
                    continue;
                }

                var keyStart = index;
                while (index < text.Length && IsAttributeKeyChar(text[index]))
                {
                    index++;
                }

                var keyEnd = index;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                if (index >= text.Length || text[index] != '=')
                {
                    index = keyEnd;
                    continue;
                }

                index++;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                if (index >= text.Length)
                {
                    break;
                }

                var value = string.Empty;
                if (text[index] is '"' or '\'')
                {
                    var quote = text[index++];
                    var builder = new System.Text.StringBuilder();
                    while (index < text.Length)
                    {
                        var ch = text[index++];
                        if (ch == '\\' && index < text.Length)
                        {
                            var escaped = text[index++];
                            builder.Append(escaped);
                            continue;
                        }

                        if (ch == quote)
                        {
                            if (index < text.Length && text[index] == quote)
                            {
                                builder.Append(quote);
                                index++;
                                continue;
                            }

                            break;
                        }

                        builder.Append(ch);
                    }

                    value = builder.ToString();
                }
                else
                {
                    var valueStart = index;
                    while (index < text.Length && !char.IsWhiteSpace(text[index]))
                    {
                        index++;
                    }

                    value = text[valueStart..index];
                }

                tokens.Add(new AttributeToken(
                    text[keyStart..keyEnd],
                    value,
                    keyStart,
                    index));
            }

            return tokens;
        }

        private static string InferDisplayNameWithoutComma(string line, IReadOnlyList<AttributeToken> tokens)
        {
            string candidate;
            if (tokens.Count > 0)
            {
                var lastAttributeEnd = tokens.Max(token => token.End);
                candidate = lastAttributeEnd < line.Length ? line[lastAttributeEnd..] : string.Empty;
            }
            else
            {
                candidate = StripExtinfDuration(line);
            }

            return candidate.Trim().TrimStart(',').Trim();
        }

        private static string StripExtinfDuration(string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0 || colonIndex >= line.Length - 1)
            {
                return line;
            }

            var index = colonIndex + 1;
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            while (index < line.Length && line[index] != ',' && !char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            if (index < line.Length && line[index] == ',')
            {
                index++;
            }

            return index < line.Length ? line[index..] : string.Empty;
        }

        private static string StripBom(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.TrimStart('\uFEFF');
        }

        private static bool IsAttributeKeyChar(char value)
        {
            return char.IsLetterOrDigit(value) || value is '_' or '-';
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

        private sealed record AttributeToken(string Key, string Value, int Start, int End);
    }
}

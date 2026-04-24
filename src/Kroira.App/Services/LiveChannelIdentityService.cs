#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kroira.App.Services
{
    public interface ILiveChannelIdentityService
    {
        LiveChannelIdentity Build(string name, string providerEpgChannelId);
        string NormalizeExactKey(string value);
        string NormalizeAliasKey(string value);
        string NormalizeForEpgScheduleMatch(string value);
        string NormalizeForChannelDisplayDedupe(string value);
        string NormalizeForPlaybackIdentity(string value);
        IReadOnlyList<string> BuildAliasKeys(params string?[] values);
        double ComputeDiceCoefficient(string left, string right);
    }

    public sealed class LiveChannelIdentityService : ILiveChannelIdentityService
    {
        private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex QualityRegex = new(@"\b(?:hd|fhd|uhd|sd|4k|hevc|h\.?265|h265|x265|x\.?265)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CodecRegex = new(@"\b(?:hevc|h\.?265|h265|x265|x\.?265)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IptvNoiseRegex = new(@"\b(?:vip|backup|yedek|alt|source|copy)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LeadingRegionPrefixRegex = new(@"^\s*(?:tr|turkey|turkiye)\s*[:\|\-_/]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SeparatorRegex = new(@"[\|\-_/\.]+", RegexOptions.Compiled);
        private static readonly Regex AliasNoiseRegex = new(@"\b(?:tv|television|channel)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RegionNoiseRegex = new(@"\b(?:us|usa|uk|u\.k|u\.s|tr|turkiye|turkey)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BracketNoiseRegex = new(@"[\(\[\{].*?[\)\]\}]", RegexOptions.Compiled);
        private static readonly Regex NonAlphaNumericRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly IReadOnlyDictionary<string, string> NumberWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = "1",
            ["two"] = "2",
            ["three"] = "3",
            ["four"] = "4",
            ["five"] = "5",
            ["six"] = "6",
            ["seven"] = "7",
            ["eight"] = "8",
            ["nine"] = "9",
            ["ten"] = "10"
        };

        public LiveChannelIdentity Build(string name, string providerEpgChannelId)
        {
            var normalizedName = NormalizeForEpgScheduleMatch(name);
            var playbackIdentity = NormalizeForPlaybackIdentity(name);
            var aliasKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in BuildAliasKeys(name, providerEpgChannelId, normalizedName))
            {
                aliasKeys.Add(alias);
            }

            var guideAlias = NormalizeAliasKey(providerEpgChannelId);
            var preferredAlias = !string.IsNullOrWhiteSpace(guideAlias) && IsStrongGuideAlias(guideAlias)
                ? guideAlias
                : aliasKeys.FirstOrDefault(alias => !string.Equals(alias, guideAlias, StringComparison.OrdinalIgnoreCase)) ?? guideAlias;

            var identityKey = !string.IsNullOrWhiteSpace(guideAlias) && IsStrongGuideAlias(guideAlias)
                ? $"id:{guideAlias}"
                : !string.IsNullOrWhiteSpace(preferredAlias)
                    ? $"{(string.Equals(preferredAlias, guideAlias, StringComparison.OrdinalIgnoreCase) ? "id" : "name")}:{preferredAlias}"
                    : !string.IsNullOrWhiteSpace(playbackIdentity)
                        ? $"name:{playbackIdentity}"
                        : "name:unknown";

            return new LiveChannelIdentity(identityKey, normalizedName, aliasKeys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList());
        }

        public string NormalizeExactKey(string value)
        {
            return NormalizeForEpgScheduleMatch(value);
        }

        public string NormalizeForEpgScheduleMatch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = ContentClassifier.NormalizeLabel(value).Trim();
            normalized = LeadingRegionPrefixRegex.Replace(normalized, " ");
            normalized = BracketNoiseRegex.Replace(normalized, " ");
            normalized = QualityRegex.Replace(normalized, " ");
            normalized = IptvNoiseRegex.Replace(normalized, " ");
            normalized = SeparatorRegex.Replace(normalized, " ");
            normalized = MultiSpaceRegex.Replace(normalized, " ");
            return normalized.Trim().ToLowerInvariant();
        }

        public string NormalizeForChannelDisplayDedupe(string value)
        {
            return NormalizePreservingQuality(value, compact: false);
        }

        public string NormalizeForPlaybackIdentity(string value)
        {
            return NormalizePreservingQuality(value, compact: true);
        }

        public string NormalizeAliasKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = RemoveDiacritics(value).ToLowerInvariant();
            normalized = normalized.Replace("&", " and ", StringComparison.Ordinal);
            normalized = normalized.Replace("+", " plus ", StringComparison.Ordinal);
            normalized = LeadingRegionPrefixRegex.Replace(normalized, " ");
            normalized = BracketNoiseRegex.Replace(normalized, " ");
            normalized = NormalizeNumberWords(normalized);
            normalized = QualityRegex.Replace(normalized, " ");
            normalized = IptvNoiseRegex.Replace(normalized, " ");
            normalized = AliasNoiseRegex.Replace(normalized, " ");
            normalized = RegionNoiseRegex.Replace(normalized, " ");
            normalized = NonAlphaNumericRegex.Replace(normalized, " ");
            normalized = MultiSpaceRegex.Replace(normalized, " ").Trim();
            return normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
        }

        public IReadOnlyList<string> BuildAliasKeys(params string?[] values)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawValue in values)
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    continue;
                }

                var value = rawValue.Trim();
                AddAlias(keys, NormalizeDottedAliasKey(value));
                AddAlias(keys, NormalizeAliasKey(value));
                AddAlias(keys, NormalizeDottedAliasKey(BracketNoiseRegex.Replace(value, " ")));
                AddAlias(keys, NormalizeAliasKey(BracketNoiseRegex.Replace(value, " ")));

                var exactKey = NormalizeExactKey(value);
                if (!string.IsNullOrWhiteSpace(exactKey))
                {
                    AddAlias(keys, NormalizeAliasKey(exactKey));
                    if (!HasDottedRegionSuffix(value))
                    {
                        AddAlias(keys, exactKey.Replace(" ", string.Empty, StringComparison.Ordinal));
                    }
                }
            }

            return keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeDottedAliasKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = RemoveDiacritics(value).ToLowerInvariant();
            normalized = normalized.Replace("&", " and ", StringComparison.Ordinal);
            normalized = normalized.Replace("+", " plus ", StringComparison.Ordinal);
            normalized = LeadingRegionPrefixRegex.Replace(normalized, " ");
            normalized = BracketNoiseRegex.Replace(normalized, " ");
            normalized = NormalizeNumberWords(normalized);
            normalized = QualityRegex.Replace(normalized, " ");
            normalized = IptvNoiseRegex.Replace(normalized, " ");
            normalized = AliasNoiseRegex.Replace(normalized, " ");
            normalized = Regex.Replace(normalized, @"[^a-z0-9\.\-]+", " ", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s*\.\s*", ".", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s*-\s*", "-", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\.{2,}", ".", RegexOptions.IgnoreCase);
            normalized = MultiSpaceRegex.Replace(normalized, " ").Trim(' ', '.', '-');
            return normalized.Contains('.', StringComparison.Ordinal)
                ? normalized.Replace(" ", string.Empty, StringComparison.Ordinal)
                : string.Empty;
        }

        private static bool HasDottedRegionSuffix(string value)
        {
            return Regex.IsMatch(
                value.Trim(),
                @"\.(?:tr|turkey|turkiye|us|usa|uk|gb|de|fr|it|es|nl)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public double ComputeDiceCoefficient(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            var leftPairs = BuildBigrams(left);
            var rightPairs = BuildBigrams(right);
            if (leftPairs.Count == 0 || rightPairs.Count == 0)
            {
                return 0;
            }

            var intersection = leftPairs.Intersect(rightPairs, StringComparer.Ordinal).Count();
            return (2.0 * intersection) / (leftPairs.Count + rightPairs.Count);
        }

        private static void AddAlias(ISet<string> keys, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys.Add(value);
            }
        }

        private static string NormalizePreservingQuality(string value, bool compact)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = RemoveDiacritics(value).ToLowerInvariant();
            normalized = normalized.Replace("&", " and ", StringComparison.Ordinal);
            normalized = normalized.Replace("+", " plus ", StringComparison.Ordinal);
            normalized = LeadingRegionPrefixRegex.Replace(normalized, " ");
            normalized = BracketNoiseRegex.Replace(normalized, " ");
            normalized = NormalizeNumberWords(normalized);
            normalized = CodecRegex.Replace(normalized, " ");
            normalized = IptvNoiseRegex.Replace(normalized, " ");
            normalized = NonAlphaNumericRegex.Replace(normalized, " ");
            normalized = MultiSpaceRegex.Replace(normalized, " ").Trim();
            return compact
                ? normalized.Replace(" ", string.Empty, StringComparison.Ordinal)
                : normalized;
        }

        private static bool IsStrongGuideAlias(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.Length <= 2)
            {
                return false;
            }

            return value.Any(ch => !char.IsDigit(ch));
        }

        private static string NormalizeNumberWords(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(NumberWords.TryGetValue(token, out var mapped) ? mapped : token);
            }

            return builder.ToString();
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

        private static List<string> BuildBigrams(string value)
        {
            var compact = value.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (compact.Length < 2)
            {
                return new List<string>();
            }

            var bigrams = new List<string>(compact.Length - 1);
            for (var i = 0; i < compact.Length - 1; i++)
            {
                bigrams.Add(compact.Substring(i, 2));
            }

            return bigrams;
        }
    }

    public sealed record LiveChannelIdentity(
        string IdentityKey,
        string NormalizedName,
        IReadOnlyList<string> AliasKeys);
}

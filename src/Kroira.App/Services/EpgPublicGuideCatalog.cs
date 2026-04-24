#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public sealed class EpgPublicGuidePreset
    {
        public EpgPublicGuidePreset(string countryName, string countryCode)
        {
            CountryName = countryName;
            CountryCode = countryCode.ToUpperInvariant();
            Url = EpgPublicGuideCatalog.BuildIptvEpgOrgUrl(countryCode);
            Label = $"IPTV-EPG.org {CountryName} ({CountryCode})";
        }

        public string Label { get; }
        public string CountryName { get; }
        public string CountryCode { get; }
        public string Url { get; }

        public override string ToString() => Label;
    }

    public sealed class EpgPublicGuideInferenceResult
    {
        public EpgPublicGuideInferenceResult(EpgPublicGuidePreset preset, int evidenceCount, IReadOnlyList<string> evidence)
        {
            Preset = preset;
            EvidenceCount = evidenceCount;
            Evidence = evidence;
        }

        public EpgPublicGuidePreset Preset { get; }
        public int EvidenceCount { get; }
        public IReadOnlyList<string> Evidence { get; }
        public string EvidenceSummary => Evidence.Count == 0
            ? $"{EvidenceCount:N0} source hint(s)"
            : $"{EvidenceCount:N0} source hint(s): {string.Join(", ", Evidence.Take(3))}";
    }

    public static class EpgPublicGuideCatalog
    {
        private const string IptvEpgOrgHost = "iptv-epg.org";
        private static readonly Regex TokenRegex = new("[A-Z0-9]+", RegexOptions.Compiled);

        private static readonly IReadOnlyDictionary<string, string> IptvEpgOrgCountries =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AL"] = "Albania",
                ["AR"] = "Argentina",
                ["AM"] = "Armenia",
                ["AU"] = "Australia",
                ["AT"] = "Austria",
                ["BY"] = "Belarus",
                ["BE"] = "Belgium",
                ["BA"] = "Bosnia & Herzegovina",
                ["BR"] = "Brazil",
                ["BG"] = "Bulgaria",
                ["CA"] = "Canada",
                ["CL"] = "Chile",
                ["CO"] = "Colombia",
                ["HR"] = "Croatia",
                ["CZ"] = "Czech Republic",
                ["DK"] = "Denmark",
                ["FI"] = "Finland",
                ["FR"] = "France",
                ["DE"] = "Germany",
                ["GR"] = "Greece",
                ["HK"] = "Hong Kong",
                ["HU"] = "Hungary",
                ["IN"] = "India",
                ["ID"] = "Indonesia",
                ["IL"] = "Israel",
                ["IT"] = "Italy",
                ["JP"] = "Japan",
                ["MX"] = "Mexico",
                ["NL"] = "Netherlands",
                ["NZ"] = "New Zealand",
                ["NO"] = "Norway",
                ["PL"] = "Poland",
                ["PT"] = "Portugal",
                ["RO"] = "Romania",
                ["RU"] = "Russia",
                ["SA"] = "Saudi Arabia",
                ["RS"] = "Serbia",
                ["SG"] = "Singapore",
                ["ZA"] = "South Africa",
                ["KR"] = "South Korea",
                ["ES"] = "Spain",
                ["SE"] = "Sweden",
                ["CH"] = "Switzerland",
                ["TW"] = "Taiwan",
                ["TH"] = "Thailand",
                ["TR"] = "Turkey",
                ["UA"] = "Ukraine",
                ["AE"] = "United Arab Emirates",
                ["GB"] = "United Kingdom",
                ["US"] = "United States",
                ["VN"] = "Vietnam"
            };

        public static IReadOnlyList<EpgPublicGuidePreset> IptvEpgOrgPresets { get; } =
            IptvEpgOrgCountries
                .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new EpgPublicGuidePreset(pair.Value, pair.Key))
                .ToList();

        public static string BuildIptvEpgOrgUrl(string countryCode)
        {
            var normalizedCode = new string((countryCode ?? string.Empty)
                    .Where(char.IsLetter)
                    .Take(2)
                    .ToArray())
                .ToLowerInvariant();
            return string.IsNullOrWhiteSpace(normalizedCode)
                ? string.Empty
                : $"https://iptv-epg.org/files/epg-{normalizedCode}.xml";
        }

        public static string BuildGuideSourceLabel(string url, EpgGuideSourceKind kind, string fallbackLabel)
        {
            if (TryDescribeIptvEpgOrgUrl(url, out var label))
            {
                return label;
            }

            return kind switch
            {
                EpgGuideSourceKind.Public => "Public XMLTV",
                EpgGuideSourceKind.Custom => "Custom XMLTV",
                _ => fallbackLabel
            };
        }

        public static EpgGuideSourceKind ClassifyFallbackUrl(string url)
        {
            if (TryDescribeIptvEpgOrgUrl(url, out _))
            {
                return EpgGuideSourceKind.Public;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return EpgGuideSourceKind.Custom;
            }

            return uri.Scheme is "http" or "https" ? EpgGuideSourceKind.Public : EpgGuideSourceKind.Custom;
        }

        public static bool TryDescribeIptvEpgOrgUrl(string url, out string label)
        {
            label = string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Host, IptvEpgOrgHost, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileName = uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
            if (!fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".xml.gz", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var stem = fileName.EndsWith(".xml.gz", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^7]
                : fileName[..^4];
            const string prefix = "epg-";
            if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var code = stem[prefix.Length..].ToUpperInvariant();
            if (IptvEpgOrgCountries.TryGetValue(code, out var country))
            {
                label = $"IPTV-EPG.org {country} ({code})";
                return true;
            }

            if (code.Length == 2)
            {
                label = $"IPTV-EPG.org {code}";
                return true;
            }

            return false;
        }

        public static IReadOnlyList<EpgPublicGuidePreset> InferPresetsFromSourceText(IEnumerable<string> values)
        {
            return InferPresetEvidenceFromSourceText(values)
                .Select(result => result.Preset)
                .ToList();
        }

        public static IReadOnlyList<EpgPublicGuideInferenceResult> InferPresetEvidenceFromSourceText(IEnumerable<string> values)
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var evidence = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                foreach (var code in InferCountryCodes(value))
                {
                    codes.Add(code);
                    if (!evidence.TryGetValue(code, out var examples))
                    {
                        examples = new List<string>();
                        evidence[code] = examples;
                    }

                    var trimmed = value?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(trimmed) &&
                        examples.Count < 5 &&
                        !examples.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    {
                        examples.Add(trimmed.Length > 48 ? trimmed[..48] + "..." : trimmed);
                    }
                }
            }

            return IptvEpgOrgPresets
                .Where(preset => codes.Contains(preset.CountryCode))
                .Select(preset =>
                {
                    evidence.TryGetValue(preset.CountryCode, out var examples);
                    return new EpgPublicGuideInferenceResult(preset, examples?.Count ?? 0, examples?.ToList() ?? new List<string>());
                })
                .OrderByDescending(result => result.EvidenceCount)
                .ThenBy(result => result.Preset.CountryName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> InferCountryCodes(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            var normalized = NormalizeForMatching(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                yield break;
            }

            var tokens = TokenRegex.Matches(normalized)
                .Select(match => match.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool HasToken(string token) => tokens.Contains(token);
            bool HasPhrase(string phrase) => normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase);

            if (HasToken("TR") || HasToken("TURKEY") || HasToken("TURKIYE"))
            {
                yield return "TR";
            }

            if (HasToken("UK") || HasToken("GB") || HasPhrase("UNITED KINGDOM") || HasPhrase("GREAT BRITAIN"))
            {
                yield return "GB";
            }

            if (HasToken("US") || HasToken("USA") || HasPhrase("UNITED STATES"))
            {
                yield return "US";
            }

            if (HasToken("DE") || HasToken("GERMANY") || HasToken("DEUTSCHLAND"))
            {
                yield return "DE";
            }

            if (HasToken("FR") || HasToken("FRANCE"))
            {
                yield return "FR";
            }

            if (HasToken("IT") || HasToken("ITALY") || HasToken("ITALIA"))
            {
                yield return "IT";
            }

            if (HasToken("ES") || HasToken("SPAIN") || HasToken("ESPANA"))
            {
                yield return "ES";
            }

            if (HasToken("NL") || HasToken("NETHERLANDS") || HasToken("HOLLAND"))
            {
                yield return "NL";
            }

            if (HasToken("AR") || HasToken("ARAB") || HasToken("ARABIC") || HasToken("MENA") || HasPhrase("MIDDLE EAST"))
            {
                yield return "SA";
                yield return "AE";
            }

            foreach (var preset in IptvEpgOrgPresets)
            {
                if (tokens.Contains(preset.CountryCode) || HasPhrase(NormalizeForMatching(preset.CountryName)))
                {
                    yield return preset.CountryCode;
                }
            }
        }

        private static string NormalizeForMatching(string value)
        {
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
            }

            return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), "\\s+", " ").Trim();
        }

        public static IReadOnlyList<string> SplitGuideUrls(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}

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
    public interface ICatalogTaxonomyService
    {
        CatalogTaxonomyResult ResolveMovieCategory(string? categoryName, string? rawSourceCategoryName, string? title, string? originalLanguage);
        CatalogTaxonomyResult ResolveSeriesCategory(string? categoryName, string? rawSourceCategoryName, string? title, string? originalLanguage);
        CatalogTaxonomyResult ResolveLiveCategory(string? rawCategoryName, string? channelName);
        LiveChannelPresentationResult ResolveLiveChannelPresentation(string? rawName);
    }

    public sealed class CatalogTaxonomyService : ICatalogTaxonomyService
    {
        private static readonly Regex MultiWhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex BoundaryDecorationRegex = new(@"^[\s\-\|\*#=~_:\.\+]+|[\s\-\|\*#=~_:\.\+]+$", RegexOptions.Compiled);
        private static readonly Regex WrappedNoiseRegex = new(@"\s*[\[\(\{](?<tag>[^\]\)\}]+)[\]\)\}]\s*$", RegexOptions.Compiled);
        private static readonly Regex YearOnlyRegex = new(@"^(?:19|20)\d{2}$", RegexOptions.Compiled);
        private static readonly Regex YearBucketRegex = new(@"^(?:(?:19|20)\d{2})(?:\s*[-/]\s*(?:19|20)\d{2})?$", RegexOptions.Compiled);
        private static readonly Regex LiveSeparatorNoiseRegex =
            new(@"\s*(?:\||/|-)\s*(?:backup|yedek|cdn\s*\d+|server\s*\d+|line\s*\d+|source\s*\d+|alt|auto|vip(?:\s*\d+)?|hevc|h\.?264|h\.?265|4k|uhd|fhd|hd|sd|50fps|60fps|ott|raw|main)\s*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LiveTrailingTokenNoiseRegex =
            new(@"\s+(?:backup|yedek|vip(?:\s*\d+)?|hevc|h\.?264|h\.?265|4k|uhd|fhd|hd|sd|50fps|60fps|ott|raw|main|cdn\s*\d+|server\s*\d+)\s*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> AdultTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "adult", "adults", "xxx", "18+", "18 plus", "18plus", "porn", "porno", "erotic", "sex", "softcore", "hardcore"
        };

        private static readonly string[] PlatformPhrases =
        {
            "netflix", "disney+", "disney plus", "disney", "prime video", "amazon prime", "amazon",
            "exxen", "blutv", "blu tv", "gain", "hbo", "max", "apple tv", "appletv",
            "paramount", "paramount+", "puhu", "tabii", "tod", "mubi", "hulu", "peacock"
        };

        private static readonly string[] CollectionPhrases =
        {
            "collection", "collections", "boxset", "box set", "boxsets", "saga", "universe", "franchise"
        };

        private static readonly string[] DocumentaryPhrases =
        {
            "documentary", "documentaries", "docu", "belgesel", "history", "nature", "science"
        };

        private static readonly string[] KidsPhrases =
        {
            "kids", "kid", "children", "child", "cartoon", "animation", "anime", "family", "cocuk", "cocuklar"
        };

        private static readonly string[] LocalPhrases =
        {
            "yerli", "local", "turk", "turkish", "turkiye", "turkiye yapim", "anadolu"
        };

        private static readonly string[] ForeignPhrases =
        {
            "yabanci", "foreign", "international", "english", "arabic", "german", "french", "italian",
            "spanish", "korean", "japanese", "hindi", "pakistani", "russian"
        };

        private static readonly string[] GenericProviderPhrases =
        {
            "all", "all movies", "all series", "movies", "movie", "films", "film", "series", "shows",
            "vod", "vod library", "vod movies", "vod series", "general", "misc", "miscellaneous", "other",
            "others", "uncategorized", "unknown", "provider", "playlist", "package", "server", "backup",
            "test", "trial", "separator", "info", "new", "new movies", "new series", "platform", "platforms"
        };

        private static readonly string[] NewsPhrases =
        {
            "news", "haber", "business news", "world news", "gundem", "gundem", "breaking news"
        };

        private static readonly string[] MovieChannelPhrases =
        {
            "movie", "movies", "film", "films", "cinema", "sinema"
        };

        private static readonly string[] EntertainmentPhrases =
        {
            "entertainment", "general", "lifestyle", "reality", "show", "shows", "family", "tv"
        };

        private static readonly string[] MusicPhrases =
        {
            "music", "radio", "hits", "concert", "mtv", "dj"
        };

        private static readonly string[] InternationalPhrases =
        {
            "english", "arabic", "german", "french", "italian", "spanish", "russian",
            "pakistani", "indian", "korean", "japanese", "africa", "balkan", "international"
        };

        private static readonly Dictionary<string, string> GenreDisplayMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["action"] = "Action",
            ["adventure"] = "Adventure",
            ["animation"] = "Animation",
            ["anime"] = "Anime",
            ["biography"] = "Biography",
            ["comedy"] = "Comedy",
            ["crime"] = "Crime",
            ["drama"] = "Drama",
            ["fantasy"] = "Fantasy",
            ["horror"] = "Horror",
            ["mystery"] = "Mystery",
            ["romance"] = "Romance",
            ["sci fi"] = "Sci-Fi",
            ["science fiction"] = "Sci-Fi",
            ["thriller"] = "Thriller",
            ["war"] = "War",
            ["western"] = "Western",
            ["history"] = "History",
            ["music"] = "Music",
            ["family"] = "Family",
            ["reality"] = "Reality"
        };

        public CatalogTaxonomyResult ResolveMovieCategory(string? categoryName, string? rawSourceCategoryName, string? title, string? originalLanguage)
        {
            return ResolveVodCategory(
                CatalogTaxonomyDomain.Movies,
                categoryName,
                rawSourceCategoryName,
                title,
                originalLanguage,
                "Movies",
                "Local Movies",
                "Foreign Movies");
        }

        public CatalogTaxonomyResult ResolveSeriesCategory(string? categoryName, string? rawSourceCategoryName, string? title, string? originalLanguage)
        {
            return ResolveVodCategory(
                CatalogTaxonomyDomain.Series,
                categoryName,
                rawSourceCategoryName,
                title,
                originalLanguage,
                "Series",
                "Local Series",
                "Foreign Series");
        }

        public CatalogTaxonomyResult ResolveLiveCategory(string? rawCategoryName, string? channelName)
        {
            var raw = NormalizeVisibleLabel(rawCategoryName);
            var normalized = NormalizeForMatching(raw);
            var channel = NormalizeForMatching(channelName);
            var combined = $"{normalized} {channel}".Trim();

            if (ContainsAnyPhrase(combined, AdultTokens))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "Adult", CatalogTaxonomySignal.Adult, "adult");
            }

            if (ContainsAnyPhrase(combined, KidsPhrases))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "Kids", CatalogTaxonomySignal.Kids, "kids");
            }

            if (ContainsAnyPhrase(combined, DocumentaryPhrases))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "Documentary", CatalogTaxonomySignal.Documentary, "documentary");
            }

            if (ContainsAnyPhrase(combined, MusicPhrases))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "Music", CatalogTaxonomySignal.Music, "music");
            }

            if (ContainsAnyPhrase(combined, NewsPhrases))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "News", CatalogTaxonomySignal.News, "news");
            }

            if (ContentClassifier.IsSportsLikeLabel(combined))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "Sports", CatalogTaxonomySignal.Sports, "sports");
            }

            if (ContainsAnyPhrase(combined, MovieChannelPhrases))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "Movies", CatalogTaxonomySignal.MovieChannels, "movie_channels");
            }

            if (ContainsAnyPhrase(normalized, InternationalPhrases))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "International", CatalogTaxonomySignal.International, "international");
            }

            if (ContainsAnyPhrase(combined, EntertainmentPhrases) || string.IsNullOrWhiteSpace(raw))
            {
                return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "Entertainment", CatalogTaxonomySignal.Entertainment, "entertainment");
            }

            return BuildResult(CatalogTaxonomyDomain.Live, raw, normalized, "Other", CatalogTaxonomySignal.Other, "other");
        }

        public LiveChannelPresentationResult ResolveLiveChannelPresentation(string? rawName)
        {
            var raw = NormalizeVisibleLabel(rawName);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new LiveChannelPresentationResult
                {
                    RawName = string.Empty,
                    DisplayName = string.Empty,
                    NormalizedName = string.Empty
                };
            }

            var working = raw;
            var hadNoise = false;

            while (true)
            {
                var previous = working;
                var wrappedMatch = WrappedNoiseRegex.Match(working);
                if (wrappedMatch.Success && IsLiveNoiseToken(wrappedMatch.Groups["tag"].Value))
                {
                    working = working[..wrappedMatch.Index].TrimEnd();
                    hadNoise = true;
                    continue;
                }

                working = LiveSeparatorNoiseRegex.Replace(working, string.Empty).TrimEnd();
                if (!string.Equals(previous, working, StringComparison.Ordinal))
                {
                    hadNoise = true;
                    continue;
                }

                var stripped = LiveTrailingTokenNoiseRegex.Replace(working, string.Empty).TrimEnd();
                if (!string.Equals(stripped, working, StringComparison.Ordinal))
                {
                    working = stripped;
                    hadNoise = true;
                    continue;
                }

                break;
            }

            working = NormalizeVisibleLabel(working);
            if (string.IsNullOrWhiteSpace(working))
            {
                working = raw;
            }

            return new LiveChannelPresentationResult
            {
                RawName = raw,
                DisplayName = working,
                NormalizedName = NormalizeForMatching(working),
                HadVariantNoise = hadNoise && !string.Equals(raw, working, StringComparison.Ordinal)
            };
        }

        private static CatalogTaxonomyResult ResolveVodCategory(
            CatalogTaxonomyDomain domain,
            string? categoryName,
            string? rawSourceCategoryName,
            string? title,
            string? originalLanguage,
            string genericLabel,
            string localLabel,
            string foreignLabel)
        {
            var raw = NormalizeVisibleLabel(string.IsNullOrWhiteSpace(rawSourceCategoryName) ? categoryName : rawSourceCategoryName);
            var normalized = NormalizeForMatching(raw);
            var fallbackCategory = NormalizeVisibleLabel(categoryName);
            var titleMatch = NormalizeForMatching(title);
            var categoryMatch = string.IsNullOrWhiteSpace(normalized) ? NormalizeForMatching(fallbackCategory) : normalized;
            var combined = $"{categoryMatch} {titleMatch}".Trim();

            if (ContainsAnyPhrase(combined, AdultTokens))
            {
                return BuildResult(domain, raw, categoryMatch, "Adult", CatalogTaxonomySignal.Adult, "adult", isAdult: true);
            }

            if (ContainsAnyPhrase(combined, KidsPhrases))
            {
                return BuildResult(domain, raw, categoryMatch, "Kids", CatalogTaxonomySignal.Kids, "kids");
            }

            if (ContainsAnyPhrase(combined, DocumentaryPhrases))
            {
                return BuildResult(domain, raw, categoryMatch, "Documentary", CatalogTaxonomySignal.Documentary, "documentary");
            }

            if (ContainsAnyPhrase(categoryMatch, PlatformPhrases))
            {
                return BuildResult(domain, raw, categoryMatch, "Platforms", CatalogTaxonomySignal.Platform, "platform", isPlatformBucket: true);
            }

            if (ContainsAnyPhrase(categoryMatch, CollectionPhrases))
            {
                return BuildResult(domain, raw, categoryMatch, "Collections", CatalogTaxonomySignal.Collection, "collection", isCollectionBucket: true);
            }

            var isGeneric = string.IsNullOrWhiteSpace(categoryMatch) ||
                            ContainsAnyPhrase(categoryMatch, GenericProviderPhrases) ||
                            ContentClassifier.IsGarbageCategoryName(raw);
            var isYearBucket = YearOnlyRegex.IsMatch(categoryMatch) || YearBucketRegex.IsMatch(categoryMatch);

            if (!isGeneric && !isYearBucket)
            {
                var mappedGenre = ResolveGenreDisplayName(categoryMatch);
                if (!string.IsNullOrWhiteSpace(mappedGenre))
                {
                    return BuildResult(domain, raw, categoryMatch, mappedGenre, CatalogTaxonomySignal.Genre, "genre");
                }
            }

            if (ShouldUseLocalBucket(categoryMatch, originalLanguage))
            {
                return BuildResult(domain, raw, categoryMatch, localLabel, CatalogTaxonomySignal.LocalLanguage, "local_language", isLowSignal: isGeneric || isYearBucket, isYearBucket: isYearBucket);
            }

            if (ShouldUseForeignBucket(categoryMatch, originalLanguage))
            {
                return BuildResult(domain, raw, categoryMatch, foreignLabel, CatalogTaxonomySignal.ForeignLanguage, "foreign_language", isLowSignal: isGeneric || isYearBucket, isYearBucket: isYearBucket);
            }

            if (isGeneric || isYearBucket)
            {
                return BuildResult(domain, raw, categoryMatch, genericLabel, CatalogTaxonomySignal.Generic, isYearBucket ? "year_bucket" : "generic", isPseudoBucket: isGeneric, isLowSignal: true, isYearBucket: isYearBucket);
            }

            var display = string.IsNullOrWhiteSpace(fallbackCategory)
                ? genericLabel
                : NormalizeVisibleLabel(fallbackCategory);
            return BuildResult(domain, raw, categoryMatch, display, CatalogTaxonomySignal.Other, "raw_fallback");
        }

        private static CatalogTaxonomyResult BuildResult(
            CatalogTaxonomyDomain domain,
            string raw,
            string normalized,
            string display,
            CatalogTaxonomySignal signal,
            string appliedRule,
            bool isPseudoBucket = false,
            bool isLowSignal = false,
            bool isPlatformBucket = false,
            bool isCollectionBucket = false,
            bool isYearBucket = false,
            bool isAdult = false)
        {
            return new CatalogTaxonomyResult
            {
                Domain = domain,
                RawCategoryName = string.IsNullOrWhiteSpace(raw) ? "Uncategorized" : raw,
                NormalizedSourceCategoryName = string.IsNullOrWhiteSpace(normalized) ? NormalizeForMatching(raw) : normalized,
                DisplayCategoryName = string.IsNullOrWhiteSpace(display) ? "Uncategorized" : display,
                PrimarySignal = signal,
                AppliedRule = appliedRule,
                IsPseudoBucket = isPseudoBucket,
                IsLowSignal = isLowSignal,
                IsPlatformBucket = isPlatformBucket,
                IsCollectionBucket = isCollectionBucket,
                IsYearBucket = isYearBucket,
                IsAdult = isAdult
            };
        }

        private static string ResolveGenreDisplayName(string normalizedCategory)
        {
            foreach (var pair in GenreDisplayMap)
            {
                if (ContainsPhrase(normalizedCategory, pair.Key))
                {
                    return pair.Value;
                }
            }

            return string.Empty;
        }

        private static bool ShouldUseLocalBucket(string normalizedCategory, string? originalLanguage)
        {
            return ContainsAnyPhrase(normalizedCategory, LocalPhrases) ||
                   string.Equals(originalLanguage?.Trim(), "tr", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldUseForeignBucket(string normalizedCategory, string? originalLanguage)
        {
            if (ContainsAnyPhrase(normalizedCategory, ForeignPhrases))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(originalLanguage) &&
                   !string.Equals(originalLanguage.Trim(), "tr", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLiveNoiseToken(string value)
        {
            var normalized = NormalizeForMatching(value);
            return ContainsAnyPhrase(normalized, new[]
            {
                "backup", "yedek", "vip", "main", "ott", "raw", "hevc", "h264", "h265", "4k", "uhd", "fhd", "hd", "sd"
            }) || Regex.IsMatch(normalized, @"^(?:cdn|server)\s*\d+$", RegexOptions.IgnoreCase);
        }

        private static bool ContainsAnyPhrase(string value, IEnumerable<string> phrases)
        {
            foreach (var phrase in phrases)
            {
                if (ContainsPhrase(value, phrase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPhrase(string value, string phrase)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(phrase))
            {
                return false;
            }

            var haystack = $" {NormalizeForMatching(value)} ";
            var needle = $" {NormalizeForMatching(phrase)} ";
            return haystack.Contains(needle, StringComparison.Ordinal);
        }

        private static string NormalizeVisibleLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = ContentClassifier.NormalizeLabel(value);
            cleaned = BoundaryDecorationRegex.Replace(cleaned, string.Empty);
            cleaned = MultiWhitespaceRegex.Replace(cleaned, " ").Trim();
            return MaybeTitleCase(cleaned);
        }

        private static string NormalizeForMatching(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = RemoveDiacritics(ContentClassifier.NormalizeLabel(value)).ToLowerInvariant();
            var buffer = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                buffer.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            return MultiWhitespaceRegex.Replace(buffer.ToString(), " ").Trim();
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

        private static string MaybeTitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var hasLetters = value.Any(char.IsLetter);
            var looksAllCaps = hasLetters && value.Where(char.IsLetter).All(char.IsUpper);
            if (!looksAllCaps)
            {
                return value.Trim();
            }

            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()).Trim();
        }
    }
}

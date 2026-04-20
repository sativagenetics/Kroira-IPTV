using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    /// <summary>
    /// Central content-classification helper used by both M3U and Xtream parsers.
    ///
    /// M3U-specific safety additions (buckets, adult, featured guards) are clearly
    /// separated from the shared Xtream helpers. Xtream callers are unaffected by
    /// every new M3U method.
    /// </summary>
    public static class ContentClassifier
    {
        // ── Extension sets ────────────────────────────────────────────────

        private static readonly HashSet<string> MovieExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "mp4", "mkv", "avi", "mov", "m4v", "mpg", "mpeg", "wmv", "flv", "webm"
        };

        private static readonly HashSet<string> NonMovieExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "ts", "m3u8", "m3u", "php", "txt", "html", "htm"
        };

        // ── M3U bucket / adult / trash blocklist ──────────────────────────
        //
        // Any group-title or item title that matches one of the patterns below
        // must NOT become:
        //   • a Movie hero candidate
        //   • a Series title
        //   • a featured item
        //   • a show card in any Home rail
        //
        // This list covers:
        //   – generic "ALL" / "ALL CHANNELS" bucket labels
        //   – adult / XXX content buckets
        //   – country / language routing labels used by providers
        //   – provider / platform labels
        //   – obvious placeholder / test / garbage rows
        //
        // Match strategy: exact normalized lower-case full match, OR
        // contains-match for well-known toxic prefixes/substrings listed
        // separately below.

        private static readonly HashSet<string> _bucketExact = new(StringComparer.OrdinalIgnoreCase)
        {
            // English generic buckets
            "all", "all channels", "all movies", "all series", "all vod",
            "vod", "vod library", "vod content", "movies", "series", "shows",
            "movie", "film", "films",
            "live tv", "live channels", "channels", "live",
            "new", "new movies", "new releases", "new series",
            "recently added", "popular", "top rated", "trending",
            "featured", "highlights", "recommended",
            "adult", "adults", "xxx", "18+", "18 +", "18plus",
            "uncategorized", "other", "others", "misc", "miscellaneous",
            "unknown", "unknown movie", "unknown series", "unknown channel",
            "test", "demo", "placeholder", "sample",
            "4k", "fhd", "hd", "sd", "uhd", "hevc", "h265",

            // German
            "serien", "filme", "filmen", "kanale", "kanäle",
            "alle", "alle filme", "alle serien", "alle kanale", "alle kanäle",
            "all serien", "all filme", "all filmen", "all kanale",

            // Turkish
            "diziler", "filmler", "kanallar",
            "tum diziler", "tüm diziler", "tum filmler", "tüm filmler",
            "all diziler", "all filmler", "all kanallar",

            // Spanish / Portuguese
            "películas", "peliculas", "filmes", "canais", "canales",
            "séries", "series tv", "todos", "todo",
            "all peliculas", "all películas", "all filmes",
            "all séries", "all canales", "all canais",

            // French
            "chaines", "chaînes", "films vf", "séries vf",
            "all chaines", "all chaînes",

            // Italian
            "canali", "serie tv",
            "all canali", "all serie",

            // Polish / Czech / Slovak / Hungarian (common labels only)
            "filmy", "seriale", "kanaly", "kanały",
            "all filmy", "all seriale",
        };

        // Toxic substrings: any group/title containing one of these is a bucket.
        private static readonly string[] _bucketContains =
        {
            "xxx",
            "adult",
            "18+",
            "erotic",
            "softcore",
            "hardcore",
            "| all",
            "all |",
            "- all",
            "all -",
            "| vod",
            "vod |",
            "playlist",
            "package",
            "reseller",
            "credits",
            "trial",
            "iptv pack",
            "provider",
            "stream pack",
            "channel pack",
        };

        // Country / language routing labels commonly used as group-titles in
        // M3U playlists. These must not become series or movie categories.
        // We only block them when they appear as the ENTIRE group-title or
        // as a standalone item title, not when they are part of a real title.
        private static readonly HashSet<string> _countryBuckets = new(StringComparer.OrdinalIgnoreCase)
        {
            // Broad language/region labels
            "english", "turkish", "arabic", "french", "german", "spanish",
            "italian", "portuguese", "dutch", "russian", "greek", "polish",
            "romanian", "czech", "slovak", "hungarian", "swedish", "norwegian",
            "danish", "finnish", "hebrew", "persian", "urdu", "hindi",
            "bengali", "tamil", "telugu", "malayalam", "kannada", "marathi",
            "punjabi", "gujarati", "korean", "japanese", "chinese", "thai",
            "indonesian", "malay",
            // Country codes / names used as M3U group-title buckets
            "tr", "uk", "us", "de", "fr", "es", "it", "pt", "nl", "ru",
            "pl", "ro", "gr", "cz", "sk", "hu", "se", "no", "dk", "fi",
            "ar", "he", "fa", "ur", "hi", "ko", "ja", "zh", "th",
            "turkey", "united kingdom", "united states", "germany",
            "france", "spain", "italy", "portugal", "netherlands",
            "russia", "poland", "romania", "greece", "czech republic",
            "hungary", "sweden", "norway", "denmark", "finland",
            "saudi arabia", "uae", "iran", "pakistan", "india",
            "korea", "japan", "china", "thailand", "indonesia", "malaysia",
        };

        // ── M3U bucket detection ──────────────────────────────────────────

        /// <summary>
        /// Returns true when a group-title or item title is a provider bucket,
        /// country label, adult category, or other non-content label that must
        /// never enter the catalog as a real item identity.
        ///
        /// Use this for <b>both</b> group-title filtering and title-level safety.
        /// The check is applied against the <em>raw</em> value AND against a
        /// divider-stripped variant so that provider rows like
        /// "── MOVIES ──", "=== VOD ===", or "### ADULT ###" are caught the
        /// same as plain "MOVIES" / "VOD" / "ADULT".
        /// </summary>
        public static bool IsM3uBucketOrAdultLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            var lower = value.Trim().ToLowerInvariant();

            if (MatchesBucketWithStripping(lower)) return true;

            // Divider/decoration-stripped form — handles provider "section
            // header" rows that are pure buckets wrapped in box-drawing or
            // ASCII art (these rows are NEVER real content).
            var dividerStripped = StripDividerDecorations(lower);
            if (!string.Equals(dividerStripped, lower, StringComparison.Ordinal) &&
                MatchesBucketWithStripping(dividerStripped))
                return true;

            return false;
        }

        /// <summary>
        /// Runs <see cref="MatchesBucket"/> on the raw value AND on the value
        /// after stripping common "ALL" / "VOD" / "NEW" / "TOP" / "BEST"
        /// leading wrappers. Catches provider patterns like:
        ///   "ALL SERIEN", "ALL | FILME", "VOD - MOVIES", "NEW SERIES",
        ///   "TOP MOVIES", "BEST FILMLER", "— ALL CHANNELS —".
        /// Also strips common trailing "PACK" / "LIBRARY" / "LIST" wrappers.
        /// Real content titles rarely look like this, and no real Movie or
        /// Series title should ever survive both the base lookup AND the
        /// stripped lookup against the exhaustive multi-language bucket set.
        /// </summary>
        private static bool MatchesBucketWithStripping(string lower)
        {
            if (MatchesBucket(lower)) return true;

            foreach (var prefix in _leadingBucketWrappers)
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var rest = lower.Substring(prefix.Length).TrimStart(' ', '-', '|', ':', '_', '.');
                    if (!string.IsNullOrEmpty(rest) && MatchesBucket(rest)) return true;
                }
            }

            foreach (var suffix in _trailingBucketWrappers)
            {
                if (lower.EndsWith(suffix, StringComparison.Ordinal))
                {
                    var rest = lower.Substring(0, lower.Length - suffix.Length).TrimEnd(' ', '-', '|', ':', '_', '.');
                    if (!string.IsNullOrEmpty(rest) && MatchesBucket(rest)) return true;
                }
            }

            return false;
        }

        private static bool MatchesBucket(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower)) return true;

            if (_bucketExact.Contains(lower)) return true;
            if (_countryBuckets.Contains(lower)) return true;

            foreach (var toxic in _bucketContains)
            {
                if (lower.Contains(toxic)) return true;
            }

            return false;
        }

        // Leading wrappers that prefix a real bucket label.
        // Any title of the form "<prefix><bucket>" (e.g. "all serien",
        // "vod movies", "new series", "top filme") is rejected.
        private static readonly string[] _leadingBucketWrappers =
        {
            "all ", "all-", "all_", "all|", "all :",
            "vod ", "vod-", "vod_", "vod|",
            "new ", "new-", "new_",
            "top ", "top-", "top_",
            "best ", "best-", "best_",
            "tum ", "tüm ", "alle ", "todos ", "todo ",
            "- all ", "| all ", "— all ", "– all ",
        };

        // Trailing wrappers that suffix a real bucket label.
        private static readonly string[] _trailingBucketWrappers =
        {
            " pack", " packs", " library", " list", " lists",
            " channels", " movies", " series",
            " - all", " | all",
        };

        // Strip common divider / decoration characters and repeated punctuation
        // used by M3U providers to render "section header" rows. Operates on
        // an already-lower-cased string and collapses whitespace.
        private static readonly Regex _dividerChars =
            new(@"[═─━•◆★☆※◎○●◇◆▲△▼▽■□♦♣♠♥|│┃┌┐└┘├┤┬┴┼=\-_\*\.#>< ]{1,}", RegexOptions.Compiled);

        private static string StripDividerDecorations(string lower)
        {
            if (string.IsNullOrWhiteSpace(lower)) return string.Empty;
            var s = _dividerChars.Replace(lower, " ").Trim();
            s = _multiSpace.Replace(s, " ");
            return s.Trim();
        }

        /// <summary>
        /// Returns true when a category name for a Movie or Series should be
        /// suppressed from the featured/hero candidate pool. This is a
        /// slightly wider gate than <see cref="IsM3uBucketOrAdultLabel"/> —
        /// it also excludes any category that looks like a generic routing
        /// bucket rather than a curated content section.
        /// </summary>
        public static bool IsM3uFeaturedUnsafeCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return true;
            return IsM3uBucketOrAdultLabel(categoryName);
        }

        /// <summary>
        /// Returns true when a Movie imported from M3U is safe to appear as a
        /// featured / hero candidate on the Home page or Movies page.
        ///
        /// Criteria:
        ///   • Real non-garbage title (≥3 chars, no placeholder markers)
        ///   • Non-bucket category name
        ///   • Non-bucket title (title must not itself be a bucket label)
        ///   • Has a playable stream URL
        /// </summary>
        public static bool IsM3uMovieFeaturedSafe(string title, string categoryName, string streamUrl)
        {
            if (IsGarbageTitle(title)) return false;
            if (string.IsNullOrWhiteSpace(streamUrl)) return false;
            if (IsPromotionalCatalogLabel(title) || IsPromotionalCatalogLabel(categoryName)) return false;
            if (IsM3uBucketOrAdultLabel(title)) return false;
            if (IsM3uFeaturedUnsafeCategory(categoryName)) return false;
            return true;
        }

        /// <summary>
        /// Source-type-aware featured-safety gate for Movies.
        /// <para>
        /// Xtream items are returned untouched — the Xtream pipeline already
        /// yields structured data and must never be filtered by M3U-specific
        /// safety rules. The M3U rules apply only to <see cref="SourceType.M3U"/>
        /// items, with a minimum guard against empty / garbage titles which is
        /// safe for both source types.
        /// </para>
        /// </summary>
        public static bool IsFeaturedSafeMovie(SourceType sourceType, string title, string categoryName, string streamUrl)
        {
            // Universal minimum: must be playable and not obviously garbage.
            if (string.IsNullOrWhiteSpace(streamUrl)) return false;
            if (IsGarbageTitle(title)) return false;
            if (IsPromotionalCatalogLabel(title) || IsPromotionalCatalogLabel(categoryName)) return false;

            // Xtream data is already clean — do not apply M3U bucket rules.
            if (sourceType == SourceType.Xtream) return true;

            return IsM3uMovieFeaturedSafe(title, categoryName, streamUrl);
        }

        /// <summary>
        /// Returns true when a Series imported from M3U is safe to appear as a
        /// featured / hero candidate on the Home page or Series page.
        /// </summary>
        public static bool IsM3uSeriesFeaturedSafe(string title, string categoryName)
        {
            if (IsGarbageTitle(title)) return false;
            if (IsPromotionalCatalogLabel(title) || IsPromotionalCatalogLabel(categoryName)) return false;
            if (IsM3uBucketOrAdultLabel(title)) return false;
            if (IsM3uFeaturedUnsafeCategory(categoryName)) return false;
            return true;
        }

        /// <summary>
        /// Source-type-aware featured-safety gate for Series. Xtream series are
        /// untouched; only M3U series are filtered by the bucket rules.
        /// </summary>
        public static bool IsFeaturedSafeSeries(SourceType sourceType, string title, string categoryName)
        {
            if (IsGarbageTitle(title)) return false;
            if (IsPromotionalCatalogLabel(title) || IsPromotionalCatalogLabel(categoryName)) return false;

            if (sourceType == SourceType.Xtream) return true;

            return IsM3uSeriesFeaturedSafe(title, categoryName);
        }

        // ── Shared helpers (Xtream + M3U) ────────────────────────────────

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

        public static bool TryExtractM3uPseudoCategoryHeader(string title, out string categoryLabel)
        {
            categoryLabel = string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var normalized = NormalizeLabel(title);
            if (normalized.Length < 3)
            {
                return false;
            }

            var lower = normalized.ToLowerInvariant();
            var stripped = StripDividerDecorations(lower);
            if (string.IsNullOrWhiteSpace(stripped) || stripped.Length < 3)
            {
                return false;
            }

            var hasDecoratedBoundary =
                Regex.IsMatch(normalized, @"^\s*[*#=\-_|~\.]{3,}", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(normalized, @"[*#=\-_|~\.]{3,}\s*$", RegexOptions.IgnoreCase);

            if (!hasDecoratedBoundary)
            {
                return false;
            }

            var words = stripped
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0 || words.Length > 6)
            {
                return false;
            }

            var looksLikeHeading = words.All(word =>
                word.All(ch => char.IsLetterOrDigit(ch) || ch is '&' or '+' or '/' or '\'' or '(' or ')' or '-'));

            if (!looksLikeHeading)
            {
                return false;
            }

            categoryLabel = NormalizeLabel(stripped.ToUpperInvariant());
            return categoryLabel.Length >= 3;
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

        public static bool IsPromotionalCatalogLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            var normalized = NormalizeLabel(value).ToLowerInvariant();
            if (normalized.Length <= 2) return false;

            return _promotionalBoundaryRegex.IsMatch(normalized);
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

        // ── M3U entry classification ──────────────────────────────────────
        //
        // Conservative rules (prefer Movie over fake Series):
        //   • tvg-type = "series"/"episode" is honored ONLY if the title carries
        //     a real episodic marker. Otherwise the item is treated as Movie/VOD.
        //   • URL containing "/series/" is honored ONLY if the title carries a
        //     real episodic marker. Otherwise Movie.
        //   • Group-title ("serie", "dizi", "show", "episode") is a soft hint.
        //     It may push a borderline item into Movie, but it CAN NEVER promote
        //     an item into Episode by itself. Episode classification requires a
        //     real marker in the title (SxxExx, NxNN, "Episode N", "Bölüm N",
        //     "Capítulo N", "Odcinek N").
        //   • Missing all signals → Live (default for live-looking streams).

        public enum M3uEntryType { Live, Movie, Episode }

        public static M3uEntryType ClassifyM3uEntry(string name, string streamUrl, string groupName, string tvgType)
        {
            var lowerUrl = (streamUrl ?? string.Empty).Trim().ToLowerInvariant();
            var lowerGroup = (groupName ?? string.Empty).Trim().ToLowerInvariant();
            var titleHasEpisode = HasEpisodePattern(name);

            // 1) tvg-type explicit wins, but "series"/"episode" requires a real
            //    episode marker in the title. Otherwise demote to Movie.
            if (!string.IsNullOrWhiteSpace(tvgType))
            {
                var t = tvgType.Trim().ToLowerInvariant();
                if (t == "movie") return M3uEntryType.Movie;
                if (t == "live") return M3uEntryType.Live;
                if (t == "series" || t == "episode")
                    return titleHasEpisode ? M3uEntryType.Episode : M3uEntryType.Movie;
            }

            // 2) URL path hints.
            if (lowerUrl.Contains("/live/")) return M3uEntryType.Live;

            if (lowerUrl.Contains("/series/"))
                return titleHasEpisode ? M3uEntryType.Episode : M3uEntryType.Movie;

            if (lowerUrl.Contains("/movie/") || lowerUrl.Contains("/vod/"))
                return titleHasEpisode ? M3uEntryType.Episode : M3uEntryType.Movie;

            // 3) File extension implies VOD-like content.
            var ext = GetUrlExtension(lowerUrl);
            if (!string.IsNullOrEmpty(ext) && MovieExtensions.Contains(ext))
                return titleHasEpisode ? M3uEntryType.Episode : M3uEntryType.Movie;

            // 4) Group-title soft hints. NEVER promote to Episode from group alone.
            bool groupSaysVod =
                lowerGroup.Contains("movie") ||
                lowerGroup.Contains("film") ||
                lowerGroup.Contains("cinema") ||
                lowerGroup.Contains("vod");

            bool groupSaysSeries =
                lowerGroup.Contains("serie") ||  // series / séries / serien
                lowerGroup.Contains("dizi") ||   // Turkish
                lowerGroup.Contains("episode") ||
                (lowerGroup.Contains("show") && !lowerGroup.Contains("showcase"));

            if (groupSaysVod)
                return titleHasEpisode ? M3uEntryType.Episode : M3uEntryType.Movie;

            if (groupSaysSeries)
            {
                // A "Series" group does NOT force Episode.
                // Without a title-level marker we treat it as standalone VOD.
                return titleHasEpisode ? M3uEntryType.Episode : M3uEntryType.Movie;
            }

            // 5) No hints at all.
            if (titleHasEpisode) return M3uEntryType.Episode;
            return M3uEntryType.Live;
        }

        // ── Episode marker detection ──────────────────────────────────────

        public static bool HasEpisodePattern(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;

            // SxxExx  → S01E05, s1e5, S01 E05, S01.E05
            if (Regex.IsMatch(title, @"\bS\d{1,2}[\s\.\-_]*E\d{1,3}\b", RegexOptions.IgnoreCase)) return true;

            // NxNN    → 1x05, 12x101  (not 4x4 — require 2+ digits after x)
            if (Regex.IsMatch(title, @"(?<![A-Za-z0-9])\d{1,2}x\d{2,3}(?![A-Za-z0-9])")) return true;

            // Episode N / Ep N / Ep. N
            if (Regex.IsMatch(title, @"\bEp(?:isode)?\.?\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Bölüm/Bolum N (Turkish)
            if (Regex.IsMatch(title, @"\bB[oö]l[uü]m\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Capítulo/Capitulo N (Spanish / Portuguese)
            if (Regex.IsMatch(title, @"\bCap[ií]tulo\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Episodio N (Spanish / Italian)
            if (Regex.IsMatch(title, @"\bEpisodio\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Folge N (German)
            if (Regex.IsMatch(title, @"\bFolge\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Odcinek N (Polish)
            if (Regex.IsMatch(title, @"\bOdcinek\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            return false;
        }

        // ── Strong-marker detection (used for grouping confidence) ────────
        //
        // A "strong" episode marker is one that the user or provider had to
        // type explicitly and that would be extremely unlikely to appear by
        // accident in a movie or live-channel title. The set of strong markers
        // matches the episode parser's supported patterns one-for-one, so any
        // title the parser can split into (series, season, episode) also
        // counts as a strong confidence signal for series grouping.
        //
        // Supported strong markers:
        //   • SxxExx                (S01E05, s1e5, S01.E05)
        //   • NxNN                  (1x05, 12x101 — minimum 2 digits after x)
        //   • Episode N / Ep N      (English)
        //   • Bölüm/Bolum N         (Turkish)
        //   • Capítulo/Capitulo N   (Spanish / Portuguese)
        //   • Episodio N            (Spanish / Italian)
        //   • Folge N               (German)
        //   • Odcinek N             (Polish)

        public static bool HasStrongEpisodeMarker(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;

            // SxxExx / NxNN — universally strong
            if (Regex.IsMatch(title, @"\bS\d{1,2}[\s\.\-_]*E\d{1,3}\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(title, @"(?<![A-Za-z0-9])\d{1,2}x\d{2,3}(?![A-Za-z0-9])")) return true;

            // Explicit "Episode N" / "Ep N" / "Ep. N"
            if (Regex.IsMatch(title, @"\bEp(?:isode)?\.?\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Turkish "Bölüm N"
            if (Regex.IsMatch(title, @"\bB[oö]l[uü]m\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Spanish / Portuguese "Capítulo N"
            if (Regex.IsMatch(title, @"\bCap[ií]tulo\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Spanish / Italian "Episodio N"
            if (Regex.IsMatch(title, @"\bEpisodio\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // German "Folge N"
            if (Regex.IsMatch(title, @"\bFolge\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            // Polish "Odcinek N"
            if (Regex.IsMatch(title, @"\bOdcinek\s*\d{1,4}\b", RegexOptions.IgnoreCase)) return true;

            return false;
        }

        // ── Episode parsing ───────────────────────────────────────────────

        public static bool TryParseEpisodeInfo(
            string title,
            out string seriesTitle,
            out int seasonNumber,
            out int episodeNumber,
            out string episodeTitle)
        {
            seriesTitle = string.Empty;
            seasonNumber = 1;
            episodeNumber = 0;
            episodeTitle = string.Empty;

            if (string.IsNullOrWhiteSpace(title)) return false;

            // 1) SxxExx — "Breaking Bad S01E05 Dead Freight"
            var m = Regex.Match(title, @"^(.*?)[\s\.\-_]+S(\d{1,2})[\s\.\-_]*E(\d{1,3})(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                seriesTitle = CleanSeriesBaseTitle(m.Groups[1].Value);
                seasonNumber = int.Parse(m.Groups[2].Value);
                episodeNumber = int.Parse(m.Groups[3].Value);
                episodeTitle = CleanEpisodeSuffix(m.Groups[4].Value);
                return !string.IsNullOrWhiteSpace(seriesTitle);
            }

            // 2) NxNN — "Breaking Bad 1x05"
            m = Regex.Match(title, @"^(.*?)[\s\.\-_]+(\d{1,2})x(\d{2,3})(.*)$");
            if (m.Success)
            {
                seriesTitle = CleanSeriesBaseTitle(m.Groups[1].Value);
                seasonNumber = int.Parse(m.Groups[2].Value);
                episodeNumber = int.Parse(m.Groups[3].Value);
                episodeTitle = CleanEpisodeSuffix(m.Groups[4].Value);
                return !string.IsNullOrWhiteSpace(seriesTitle);
            }

            // 3) "Show - Season N Episode M" / "Show Season N Ep M"
            m = Regex.Match(title, @"^(.*?)[\s\.\-_]+Season[\s\.\-_]*(\d{1,2})[\s\.\-_]+(?:Ep(?:isode)?\.?)[\s\.\-_]*(\d{1,4})(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                seriesTitle = CleanSeriesBaseTitle(m.Groups[1].Value);
                seasonNumber = int.Parse(m.Groups[2].Value);
                episodeNumber = int.Parse(m.Groups[3].Value);
                episodeTitle = CleanEpisodeSuffix(m.Groups[4].Value);
                return !string.IsNullOrWhiteSpace(seriesTitle);
            }

            // 4) "Show Ep N" / "Show Episode N"
            m = Regex.Match(title, @"^(.*?)[\s\.\-_]+Ep(?:isode)?\.?\s*(\d{1,4})(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                seriesTitle = CleanSeriesBaseTitle(m.Groups[1].Value);
                seasonNumber = 1;
                episodeNumber = int.Parse(m.Groups[2].Value);
                episodeTitle = CleanEpisodeSuffix(m.Groups[3].Value);
                return !string.IsNullOrWhiteSpace(seriesTitle);
            }

            // 5) "Show Bölüm N" (Turkish)
            m = Regex.Match(title, @"^(.*?)[\s\.\-_]+B[oö]l[uü]m\s*(\d{1,4})(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                seriesTitle = CleanSeriesBaseTitle(m.Groups[1].Value);
                seasonNumber = 1;
                episodeNumber = int.Parse(m.Groups[2].Value);
                episodeTitle = CleanEpisodeSuffix(m.Groups[3].Value);
                return !string.IsNullOrWhiteSpace(seriesTitle);
            }

            // 6) "Show Capítulo N" (Spanish / Portuguese)
            m = Regex.Match(title, @"^(.*?)[\s\.\-_]+Cap[ií]tulo\s*(\d{1,4})(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                seriesTitle = CleanSeriesBaseTitle(m.Groups[1].Value);
                seasonNumber = 1;
                episodeNumber = int.Parse(m.Groups[2].Value);
                episodeTitle = CleanEpisodeSuffix(m.Groups[3].Value);
                return !string.IsNullOrWhiteSpace(seriesTitle);
            }

            // 7) "Show Episodio N" / "Show Folge N" / "Show Odcinek N"
            m = Regex.Match(title, @"^(.*?)[\s\.\-_]+(?:Episodio|Folge|Odcinek)\s*(\d{1,4})(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                seriesTitle = CleanSeriesBaseTitle(m.Groups[1].Value);
                seasonNumber = 1;
                episodeNumber = int.Parse(m.Groups[2].Value);
                episodeTitle = CleanEpisodeSuffix(m.Groups[3].Value);
                return !string.IsNullOrWhiteSpace(seriesTitle);
            }

            return false;
        }

        // ── Title cleaning regexes ────────────────────────────────────────

        private static readonly Regex _leadingLangTag =
            new(@"^\s*(?:\[[A-Za-z]{2,4}\]|\([A-Za-z]{2,4}\)|[A-Za-z]{2,4}\s*[:\|])\s*", RegexOptions.Compiled);

        private static readonly Regex _trailingQuality =
            new(@"[\s\.\-_\|]*(?:1080p|720p|480p|2160p|4k|uhd|hdr|hevc|x264|x265|h\.?264|h\.?265|web[\-\s]?dl|webrip|bluray|bdrip|dvdrip|hdtv|multi|dual\s*audio)\b.*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _trailingSeasonSuffix =
            new(@"[\s\.\-_]*(?:-\s*)?(?:Season|Saison|Temporada|Sezon|Staffel)[\s\.\-_]*\d{1,2}\s*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _trailingLoneSeason =
            new(@"[\s\.\-_]+S\d{1,2}\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _trailingSeparators =
            new(@"[\s\.\-_\|:,;]+$", RegexOptions.Compiled);

        private static readonly Regex _leadingSeparators =
            new(@"^[\s\.\-_\|:,;]+", RegexOptions.Compiled);

        private static readonly Regex _multiSpace =
            new(@"\s{2,}", RegexOptions.Compiled);

        private static readonly Regex _promotionalBoundaryRegex =
            new(@"(?:^|[\[\(\{\|:\-~])\s*(?:(?:official|final|exclusive|extended)\s+)?(?:trailer|teaser|preview|clip|sample)s?(?=\s*(?:$|[\]\)\}\|:\-~]))",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] _sportsTokens =
        {
            "sport", "sports", "spor", "futbol", "football", "soccer", "basket", "basketball",
            "tennis", "golf", "baseball", "hockey", "motorsport", "motor sport", "formula 1", "f1",
            "motogp", "racing", "fight", "boxing", "wrestling", "ufc", "mma", "premier league",
            "champions league", "europa league", "conference league", "uefa", "bundesliga",
            "laliga", "la liga", "serie a", "ligue 1", "super lig", "süper lig", "nba", "nfl",
            "nhl", "mlb", "euroleague", "eurolig", "euroliga"
        };

        private static readonly string[] _turkishTokens =
        {
            "turk", "türk", "turkish", "turkiye", "türkiye", "turki", "anatolia", "anadolu",
            "super lig", "süper lig", "trt", "istanbul"
        };

        private static readonly Regex _turkishBoundaryRegex =
            new(@"\b(?:tr|turk(?:ish)?|türk|turkiye|türkiye)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Cleans an extracted series base title. Strips leading language tags
        /// and trailing "Season N" style suffixes without aggressively stripping
        /// real name fragments.
        /// </summary>
        public static string CleanSeriesBaseTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var s = raw.Trim();
            s = _leadingLangTag.Replace(s, string.Empty);
            s = _trailingSeasonSuffix.Replace(s, string.Empty);
            s = _trailingLoneSeason.Replace(s, string.Empty);
            s = _trailingSeparators.Replace(s, string.Empty);
            s = _leadingSeparators.Replace(s, string.Empty);
            s = _multiSpace.Replace(s, " ");
            return s.Trim();
        }

        private static string CleanEpisodeSuffix(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = raw.Trim();
            s = _trailingQuality.Replace(s, string.Empty);
            s = _trailingSeparators.Replace(s, string.Empty);
            s = _leadingSeparators.Replace(s, string.Empty);
            s = _multiSpace.Replace(s, " ");
            return s.Trim();
        }

        /// <summary>
        /// Produces a canonical lower-case grouping key for a cleaned series
        /// title. Used only for grouping equality — display uses the original
        /// cleaned title.
        /// </summary>
        public static string ComputeSeriesGroupingKey(string cleanedTitle)
        {
            if (string.IsNullOrWhiteSpace(cleanedTitle)) return string.Empty;
            var s = cleanedTitle.Trim().ToLowerInvariant();
            s = _trailingQuality.Replace(s, string.Empty);
            s = _trailingSeasonSuffix.Replace(s, string.Empty);
            s = _trailingLoneSeason.Replace(s, string.Empty);
            s = _trailingSeparators.Replace(s, string.Empty);
            s = _leadingSeparators.Replace(s, string.Empty);
            s = _multiSpace.Replace(s, " ");
            return s.Trim();
        }

        public static bool IsSportsLikeLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = NormalizeLabel(value).ToLowerInvariant();
            foreach (var token in _sportsTokens)
            {
                if (normalized.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsTurkishLikeLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = NormalizeLabel(value).ToLowerInvariant();
            if (_turkishBoundaryRegex.IsMatch(normalized))
            {
                return true;
            }

            foreach (var token in _turkishTokens)
            {
                if (normalized.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsSportsLikeChannel(string? title, string? categoryName)
        {
            return IsSportsLikeLabel(title) || IsSportsLikeLabel(categoryName);
        }

        public static bool IsTurkishSportsLikeChannel(string? title, string? categoryName)
        {
            return IsSportsLikeChannel(title, categoryName) &&
                   (IsTurkishLikeLabel(title) || IsTurkishLikeLabel(categoryName));
        }
    }
}

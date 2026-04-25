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
    public interface ISmartCategoryService
    {
        IReadOnlyList<SmartCategoryDefinition> GetDefinitions(SmartCategoryMediaType mediaType);
        SmartCategoryDefinition? GetDefinition(string categoryId);
        bool IsSmartCategoryKey(string? categoryId);
        bool Matches(string categoryId, SmartCategoryItemContext context);
        string BuildOriginalProviderGroupKey(string? providerGroupName);
        bool TryParseOriginalProviderGroupKey(string? categoryKey, out string normalizedProviderGroupName);
        string NormalizeKey(string? value);
    }

    public sealed class SmartCategoryService : ISmartCategoryService
    {
        private const string ProviderGroupPrefix = "provider:";
        private static readonly Regex MultiWhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);
        private readonly IReadOnlyList<SmartCategoryDefinition> _definitions;
        private readonly Dictionary<string, SmartCategoryDefinition> _definitionById;

        public SmartCategoryService()
        {
            _definitions = BuildDefinitions()
                .OrderBy(definition => definition.MediaType)
                .ThenBy(definition => definition.SortPriority)
                .ThenBy(definition => definition.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _definitionById = _definitions.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<SmartCategoryDefinition> GetDefinitions(SmartCategoryMediaType mediaType)
        {
            return _definitions.Where(definition => definition.MediaType == mediaType).ToList();
        }

        public SmartCategoryDefinition? GetDefinition(string categoryId)
        {
            return _definitionById.TryGetValue(categoryId, out var definition) ? definition : null;
        }

        public bool IsSmartCategoryKey(string? categoryId)
        {
            return !string.IsNullOrWhiteSpace(categoryId) && _definitionById.ContainsKey(categoryId);
        }

        public bool Matches(string categoryId, SmartCategoryItemContext context)
        {
            return _definitionById.TryGetValue(categoryId, out var definition) &&
                   definition.MediaType == context.MediaType &&
                   definition.Predicate(context);
        }

        public string BuildOriginalProviderGroupKey(string? providerGroupName)
        {
            var key = NormalizeKey(providerGroupName);
            return string.IsNullOrWhiteSpace(key) ? string.Empty : ProviderGroupPrefix + key;
        }

        public bool TryParseOriginalProviderGroupKey(string? categoryKey, out string normalizedProviderGroupName)
        {
            normalizedProviderGroupName = string.Empty;
            if (string.IsNullOrWhiteSpace(categoryKey) ||
                !categoryKey.StartsWith(ProviderGroupPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalizedProviderGroupName = categoryKey[ProviderGroupPrefix.Length..];
            return !string.IsNullOrWhiteSpace(normalizedProviderGroupName);
        }

        public string NormalizeKey(string? value)
        {
            return NormalizeForMatching(value);
        }

        private static IReadOnlyList<SmartCategoryDefinition> BuildDefinitions()
        {
            var definitions = new List<SmartCategoryDefinition>();
            AddLive(definitions);
            AddMovies(definitions);
            AddSeries(definitions);
            return definitions;
        }

        private static void AddLive(List<SmartCategoryDefinition> definitions)
        {
            var order = 0;
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.library.all", "All Channels", "Library", order++, "\uE714", "all live channels", c => true, alwaysShow: true, isAll: true));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.library.favorites", "Favorites", "Library", order++, "\uE735", "favorite live channels", c => c.IsFavorite, alwaysShow: true));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.library.recent", "Recently Watched", "Library", order++, "\uE81C", "recent live playback history", c => c.IsRecentlyWatched));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.library.priority", "Priority Watch", "Library", order++, "\uE7C1", "favorites, recent, sports, or guide-linked channels", c => c.IsFavorite || c.IsRecentlyWatched || IsSports(c) || c.HasGuideLink || c.HasMatchedGuide));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.library.guide_matched", "Guide Matched", "Library", order++, "\uE787", "channels with matched or linked guide data", c => c.HasMatchedGuide || c.HasGuideLink));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.library.no_guide", "No Guide", "Library", order++, "\uE711", "channels without guide data", c => !c.HasMatchedGuide && !c.HasGuideLink && !c.HasGuideData));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.library.hd_4k", "HD / 4K Channels", "Library", order++, "\uE7F4", "HD, FHD, UHD, 4K, or 1080/720 naming", IsHighQuality));

            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.all", "All Sports", "Sports", order++, "\uE7FC", "sports channel names or provider groups", IsSports));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.turkish", "Turkish Sports", "Sports", order++, "\uE7FC", "Turkish sports channel naming", c => IsSports(c) && IsTurkish(c)));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.now", "Live Sports Now", "Sports", order++, "\uE768", "sports channels with live/match hints in loaded guide text", c => IsSports(c) && Any(GuideText(c), "live", "match", "mac", "game", "vs", "v", "kick off", "kickoff")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.football", "Football", "Sports", order++, "\uE7FC", "football and soccer naming", c => Any(c, "football", "soccer", "futbol", "bein sports", "s sport") || IsFootballLeague(c)));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.basketball", "Basketball", "Sports", order++, "\uE7FC", "basketball, NBA, EuroLeague, or FIBA naming", c => Any(c, "basketball", "basketbol", "nba", "euroleague", "eurocup", "fiba", "ncaa")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.motorsport", "Motorsport", "Sports", order++, "\uE804", "motorsport and racing naming", c => Any(c, "motorsport", "motor sport", "formula 1", "formula one", "f1", "motogp", "nascar", "racing", "race", "rally")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.tennis", "Tennis", "Sports", order++, "\uE7FC", "tennis naming", c => Any(c, "tennis", "atp", "wta", "grand slam", "roland garros", "wimbledon")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.combat", "Combat Sports", "Sports", order++, "\uE7FC", "combat sport naming", c => Any(c, "boxing", "box", "mma", "ufc", "wwe", "fight", "combat", "kickboxing")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.extreme", "Extreme Sports", "Sports", order++, "\uE7FC", "extreme sport naming", c => Any(c, "extreme", "x games", "skate", "snowboard", "surf", "bmx")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.golf", "Golf", "Sports", order++, "\uE7FC", "golf naming", c => Any(c, "golf", "pga", "masters")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.cycling", "Cycling", "Sports", order++, "\uE7FC", "cycling naming", c => Any(c, "cycling", "cyclisme", "bike", "tour de france", "giro", "vuelta")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.winter", "Winter Sports", "Sports", order++, "\uE7FC", "winter sport naming", c => Any(c, "winter", "ski", "skiing", "snow", "biathlon", "ice hockey", "hockey")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.sports.multi", "Multi-Sport Channels", "Sports", order++, "\uE7FC", "broad sports channel brands", c => Any(c, "bein sports", "s sport", "sky sports", "espn", "eurosport", "sport tv", "supersport", "dazn")));

            AddLiveFootballLeagues(definitions, ref order);
            AddLiveBasketball(definitions, ref order);

            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.news", "News", "Channel Types", order++, "\uE900", "news and haber naming", c => Any(c, "news", "haber", "breaking news", "gundem")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.documentary", "Documentary", "Channel Types", order++, "\uE8FD", "documentary and factual naming", c => Any(c, "documentary", "documentaries", "belgesel", "history", "nature", "science")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.movies", "Movies", "Channel Types", order++, "\uE8B2", "movie channel naming", c => Any(c, "movie", "movies", "film", "cinema", "sinema")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.series_entertainment", "Series / Entertainment", "Channel Types", order++, "\uE7C3", "series and entertainment naming", c => Any(c, "series", "dizi", "show", "entertainment", "reality", "general")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.kids", "Kids", "Channel Types", order++, "\uE7EE", "kids and animation naming", c => Any(c, "kids", "children", "child", "cartoon", "animation", "cocuk", "anime")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.music", "Music", "Channel Types", order++, "\uE8D6", "music and radio brand naming", c => Any(c, "music", "muzik", "mtv", "hits", "radio", "concert")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.lifestyle", "Lifestyle", "Channel Types", order++, "\uE80F", "lifestyle and food naming", c => Any(c, "lifestyle", "life", "food", "cooking", "travel", "home", "fashion")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.shopping", "Shopping", "Channel Types", order++, "\uE719", "shopping channel naming", c => Any(c, "shopping", "shop", "qvc", "teleshopping")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.religious", "Religious", "Channel Types", order++, "\uE8F3", "religious channel naming", c => Any(c, "religion", "religious", "dini", "islam", "quran", "kuran", "christian", "church")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.local", "Local Channels", "Channel Types", order++, "\uE707", "local channel naming", c => Any(c, "local", "yerel", "regional", "bolgesel")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.national", "National Channels", "Channel Types", order++, "\uE774", "national channel naming", c => Any(c, "national", "ulusal", "trt", "kanal d", "show tv", "atv", "star tv", "now tv", "fox tv", "tv8")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.international", "International", "Channel Types", order++, "\uE774", "international channel naming", IsInternational));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.radio", "Radio", "Channel Types", order++, "\uE8D6", "radio naming", c => Any(c, "radio", "radyo", "fm")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.type.adult", "Adult / Restricted", "Channel Types", order++, "\uE72E", "adult restricted naming", IsAdult));

            AddLiveCountries(definitions, ref order);
        }

        private static void AddLiveFootballLeagues(List<SmartCategoryDefinition> definitions, ref int order)
        {
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.super_lig", "Super Lig", "Football Leagues", order++, "\uE7FC", "Super Lig and Turkish football naming", c => Any(c, "super lig", "spor toto", "turkish league", "trendyol super lig")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.premier_league", "Premier League", "Football Leagues", order++, "\uE7FC", "Premier League and EPL naming", c => Any(c, "premier league", "epl", "english premier")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.la_liga", "La Liga", "Football Leagues", order++, "\uE7FC", "La Liga naming", c => Any(c, "la liga", "laliga")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.serie_a", "Serie A", "Football Leagues", order++, "\uE7FC", "Serie A naming", c => Any(c, "serie a", "italian league")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.bundesliga", "Bundesliga", "Football Leagues", order++, "\uE7FC", "Bundesliga naming", c => Any(c, "bundesliga")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.ligue_1", "Ligue 1", "Football Leagues", order++, "\uE7FC", "Ligue 1 naming", c => Any(c, "ligue 1", "french league")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.champions_league", "Champions League", "Football Leagues", order++, "\uE7FC", "Champions League naming", c => Any(c, "champions league", "ucl")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.europa_league", "Europa League", "Football Leagues", order++, "\uE7FC", "Europa League naming", c => Any(c, "europa league", "uel")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.conference_league", "Conference League", "Football Leagues", order++, "\uE7FC", "Conference League naming", c => Any(c, "conference league", "uecl")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.turkish_cup", "Turkish Cup", "Football Leagues", order++, "\uE7FC", "Turkish Cup naming", c => Any(c, "turkish cup", "ziraat", "turkiye kupasi", "tff cup")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.international", "International Football", "Football Leagues", order++, "\uE7FC", "international football naming", c => Any(c, "world cup", "euro 202", "uefa nations", "international football", "national team", "milli takim")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.saudi", "Saudi Pro League", "Football Leagues", order++, "\uE7FC", "Saudi Pro League naming", c => Any(c, "saudi pro league", "saudi league", "roshn")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.mls", "MLS", "Football Leagues", order++, "\uE7FC", "MLS naming", c => Any(c, "mls", "major league soccer")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.eredivisie", "Eredivisie", "Football Leagues", order++, "\uE7FC", "Eredivisie naming", c => Any(c, "eredivisie")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.liga_portugal", "Liga Portugal", "Football Leagues", order++, "\uE7FC", "Liga Portugal naming", c => Any(c, "liga portugal", "portuguese league")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.libertadores", "Copa Libertadores", "Football Leagues", order++, "\uE7FC", "Copa Libertadores naming", c => Any(c, "libertadores", "copa libertadores")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.football.club_world_cup", "Club World Cup", "Football Leagues", order++, "\uE7FC", "Club World Cup naming", c => Any(c, "club world cup", "fifa club")));
        }

        private static void AddLiveBasketball(List<SmartCategoryDefinition> definitions, ref int order)
        {
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.basketball.nba", "NBA", "Basketball", order++, "\uE7FC", "NBA naming", c => Any(c, "nba", "national basketball association")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.basketball.euroleague", "EuroLeague", "Basketball", order++, "\uE7FC", "EuroLeague naming", c => Any(c, "euroleague", "euro league")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.basketball.eurocup", "EuroCup", "Basketball", order++, "\uE7FC", "EuroCup naming", c => Any(c, "eurocup", "euro cup")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.basketball.turkish", "Turkish Basketball Super League", "Basketball", order++, "\uE7FC", "Turkish basketball league naming", c => Any(c, "basketbol super ligi", "basketball super league", "bsl")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.basketball.ncaa", "NCAA Basketball", "Basketball", order++, "\uE7FC", "NCAA basketball naming", c => Any(c, "ncaa basketball", "college basketball")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.basketball.fiba", "FIBA", "Basketball", order++, "\uE7FC", "FIBA naming", c => Any(c, "fiba")));
        }

        private static void AddLiveCountries(List<SmartCategoryDefinition> definitions, ref int order)
        {
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.turkiye", "Turkiye", "Country & Language", order++, "\uE774", "Turkish channel naming", IsTurkish));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.uk", "United Kingdom", "Country & Language", order++, "\uE774", "UK channel naming", c => Any(c, "uk", "united kingdom", "british", "england", "bbc", "itv")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.us", "United States", "Country & Language", order++, "\uE774", "US channel naming", c => Any(c, "usa", "united states", "american", "abc", "nbc", "cbs", "fox us")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.germany", "Germany", "Country & Language", order++, "\uE774", "German channel naming", c => Any(c, "germany", "german", "deutsch", "deutschland")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.france", "France", "Country & Language", order++, "\uE774", "French channel naming", c => Any(c, "france", "french", "francais")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.spain", "Spain", "Country & Language", order++, "\uE774", "Spanish channel naming", c => Any(c, "spain", "spanish", "espana")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.italy", "Italy", "Country & Language", order++, "\uE774", "Italian channel naming", c => Any(c, "italy", "italian", "italia")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.netherlands", "Netherlands", "Country & Language", order++, "\uE774", "Dutch channel naming", c => Any(c, "netherlands", "dutch", "holland", "nederland")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.portugal", "Portugal", "Country & Language", order++, "\uE774", "Portuguese channel naming", c => Any(c, "portugal", "portuguese")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.arabic", "Arabic", "Country & Language", order++, "\uE774", "Arabic channel naming", c => Any(c, "arabic", "arab", "mideast", "middle east")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.balkan", "Balkan", "Country & Language", order++, "\uE774", "Balkan channel naming", c => Any(c, "balkan", "serbia", "croatia", "bosnia", "albania", "macedonia")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.latin", "Latin America", "Country & Language", order++, "\uE774", "Latin American channel naming", c => Any(c, "latin", "latino", "latam", "mexico", "argentina", "brazil", "colombia")));
            definitions.Add(Category(SmartCategoryMediaType.Live, "live.country.world", "World Channels", "Country & Language", order++, "\uE774", "world and international channel naming", c => Any(c, "world", "international", "global") || IsInternational(c)));
        }

        private static void AddMovies(List<SmartCategoryDefinition> definitions)
        {
            var order = 10_000;
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.all", "All Movies", "Library", order++, "\uE8B2", "all movies", c => true, alwaysShow: true, isAll: true));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.continue", "Continue Watching", "Library", order++, "\uE768", "movies with resume progress", c => c.IsContinueWatching || c.IsInProgress, alwaysShow: true));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.favorites", "Favorites", "Library", order++, "\uE735", "favorite movies", c => c.IsFavorite, alwaysShow: true));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.recently_added", "Recently Added", "Library", order++, "\uE823", "source sync in the last 14 days", IsRecentlyAdded));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.recently_watched", "Recently Watched", "Library", order++, "\uE81C", "recent movie playback", c => c.IsRecentlyWatched));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.popular", "Popular", "Library", order++, "\uE7C1", "TMDb popularity signal", c => c.Popularity >= 20));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.top_rated", "Top Rated", "Library", order++, "\uE734", "TMDb vote average 7.5 or higher", c => c.VoteAverage >= 7.5));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.unwatched", "Unwatched", "Library", order++, "\uE8A7", "no watched state", c => !c.IsWatched && !c.IsInProgress));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.watched", "Watched", "Library", order++, "\uE73E", "watched movies", c => c.IsWatched));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.metadata", "Metadata Matched", "Library", order++, "\uE946", "TMDb or IMDb metadata exists", c => c.HasMetadata));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.library.missing_artwork", "Missing Artwork", "Library", order++, "\uEB9F", "poster or backdrop is missing", c => !c.HasArtwork));

            AddGenreDefinitions(definitions, SmartCategoryMediaType.Movie, ref order, "Genres", movieGenreNames);
            AddMovieCountries(definitions, ref order);
            AddMoviePlatforms(definitions, ref order);
            AddMovieCollections(definitions, ref order);
            AddMovieYearsAndQuality(definitions, ref order);
        }

        private static void AddSeries(List<SmartCategoryDefinition> definitions)
        {
            var order = 20_000;
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.all", "All Series", "Library", order++, "\uE8A9", "all series", c => true, alwaysShow: true, isAll: true));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.continue", "Continue Watching", "Library", order++, "\uE768", "shows with resume progress", c => c.IsContinueWatching || c.IsInProgress, alwaysShow: true));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.favorites", "Favorites", "Library", order++, "\uE735", "favorite series", c => c.IsFavorite, alwaysShow: true));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.recently_added", "Recently Added", "Library", order++, "\uE823", "source sync in the last 14 days", IsRecentlyAdded));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.recently_watched", "Recently Watched", "Library", order++, "\uE81C", "recent episode playback", c => c.IsRecentlyWatched));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.unwatched", "Unwatched", "Library", order++, "\uE8A7", "no watched state", c => !c.IsWatched && !c.IsInProgress));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.in_progress", "In Progress", "Library", order++, "\uE768", "series with resume or partial progress", c => c.IsInProgress));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.completed_by_me", "Completed by Me", "Library", order++, "\uE73E", "series fully watched by the active profile", c => c.IsCompleted || c.IsWatched));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.metadata", "Metadata Matched", "Library", order++, "\uE946", "TMDb or IMDb metadata exists", c => c.HasMetadata));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.library.missing_artwork", "Missing Artwork", "Library", order++, "\uEB9F", "poster or backdrop is missing", c => !c.HasArtwork));

            AddGenreDefinitions(definitions, SmartCategoryMediaType.Series, ref order, "Genres", seriesGenreNames);
            AddTurkishSeries(definitions, ref order);
            AddSeriesPlatforms(definitions, ref order);
            AddSeriesCountries(definitions, ref order);
            AddSeriesStatus(definitions, ref order);
        }

        private static readonly (string Name, string[] Phrases)[] movieGenreNames =
        {
            ("Action", new[] { "action", "aksiyon" }),
            ("Adventure", new[] { "adventure", "macera" }),
            ("Animation", new[] { "animation", "animated", "animasyon" }),
            ("Comedy", new[] { "comedy", "komedi" }),
            ("Crime", new[] { "crime", "suc" }),
            ("Documentary", new[] { "documentary", "belgesel" }),
            ("Drama", new[] { "drama", "dram" }),
            ("Family", new[] { "family", "aile" }),
            ("Fantasy", new[] { "fantasy", "fantastik" }),
            ("History", new[] { "history", "historical", "tarih" }),
            ("Horror", new[] { "horror", "korku" }),
            ("Music", new[] { "music", "muzik" }),
            ("Mystery", new[] { "mystery", "gizem" }),
            ("Romance", new[] { "romance", "romantic", "romantik" }),
            ("Science Fiction", new[] { "science fiction", "sci fi", "scifi", "bilim kurgu" }),
            ("Thriller", new[] { "thriller", "gerilim" }),
            ("War", new[] { "war", "savas" }),
            ("Western", new[] { "western" }),
            ("Biography", new[] { "biography", "biopic", "biyografi" }),
            ("Sport Movies", new[] { "sport movie", "sports movie", "spor" }),
            ("Musical", new[] { "musical", "muzikal" }),
            ("Noir", new[] { "noir" }),
            ("Superhero", new[] { "superhero", "super hero", "marvel", "dc universe" }),
            ("Disaster", new[] { "disaster", "felaket" }),
            ("Survival", new[] { "survival", "hayatta kalma" }),
            ("Martial Arts", new[] { "martial arts", "karate", "kung fu" }),
            ("Spy / Espionage", new[] { "spy", "espionage", "ajan" }),
            ("Gangster / Mafia", new[] { "gangster", "mafia", "mafya" }),
            ("Psychological Thriller", new[] { "psychological thriller", "psikolojik gerilim" }),
            ("Road Movie", new[] { "road movie" })
        };

        private static readonly (string Name, string[] Phrases)[] seriesGenreNames =
        {
            ("Action & Adventure", new[] { "action", "adventure", "aksiyon", "macera" }),
            ("Animation", new[] { "animation", "animated", "animasyon" }),
            ("Comedy", new[] { "comedy", "komedi", "sitcom" }),
            ("Crime", new[] { "crime", "suc", "detective", "police" }),
            ("Documentary Series", new[] { "documentary", "docuseries", "belgesel" }),
            ("Drama", new[] { "drama", "dram" }),
            ("Family", new[] { "family", "aile" }),
            ("Fantasy", new[] { "fantasy", "fantastik" }),
            ("Historical Drama", new[] { "historical drama", "history", "period drama", "tarihi" }),
            ("Horror", new[] { "horror", "korku" }),
            ("Mystery", new[] { "mystery", "gizem" }),
            ("Romance", new[] { "romance", "romantic", "romantik" }),
            ("Science Fiction", new[] { "science fiction", "sci fi", "scifi", "bilim kurgu" }),
            ("Thriller", new[] { "thriller", "gerilim" }),
            ("War & Politics", new[] { "war", "politics", "savas", "politik" }),
            ("Reality", new[] { "reality" }),
            ("Talk Shows", new[] { "talk show", "talkshow" }),
            ("Game Shows", new[] { "game show", "quiz" }),
            ("Soap Opera", new[] { "soap", "soap opera", "daily series", "gunluk dizi" }),
            ("Kids Series", new[] { "kids", "children", "cocuk" }),
            ("Anime", new[] { "anime" }),
            ("K-Drama", new[] { "k drama", "korean drama", "kdrama" }),
            ("Mini Series", new[] { "mini series", "miniseries", "limited series" }),
            ("Sitcom", new[] { "sitcom" }),
            ("Medical Drama", new[] { "medical drama", "doctor", "hospital" }),
            ("Legal Drama", new[] { "legal drama", "lawyer", "court" }),
            ("Police / Detective", new[] { "police", "detective", "cop", "dedektif" }),
            ("Supernatural", new[] { "supernatural", "paranormal" }),
            ("Teen Drama", new[] { "teen", "teen drama", "youth" })
        };

        private static void AddGenreDefinitions(List<SmartCategoryDefinition> definitions, SmartCategoryMediaType mediaType, ref int order, string sectionName, IEnumerable<(string Name, string[] Phrases)> genres)
        {
            foreach (var genre in genres)
            {
                var phrases = genre.Phrases;
                var key = $"{(mediaType == SmartCategoryMediaType.Movie ? "movie" : "series")}.genre.{NormalizeForMatching(genre.Name).Replace(' ', '_')}";
                definitions.Add(Category(mediaType, key, genre.Name, sectionName, order++, "\uE8FD", $"{genre.Name} genre metadata or provider naming", c => Any(c, phrases)));
            }
        }

        private static void AddMovieCountries(List<SmartCategoryDefinition> definitions, ref int order)
        {
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.turkish", "Turkish Movies", "Country & Language", order++, "\uE774", "Turkish language or provider naming", c => IsTurkish(c)));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.hollywood", "Hollywood", "Country & Language", order++, "\uE8B2", "US, Hollywood, or English-language provider naming", c => Any(c, "hollywood", "usa", "us movies", "american") || Language(c, "en")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.european", "European Movies", "Country & Language", order++, "\uE774", "European country naming", c => Any(c, "european", "europe", "france", "germany", "italy", "spain", "nordic")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.korean", "Korean Movies", "Country & Language", order++, "\uE774", "Korean movie naming", c => Any(c, "korean", "korea") || Language(c, "ko")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.japanese", "Japanese Movies", "Country & Language", order++, "\uE774", "Japanese movie naming", c => Any(c, "japanese", "japan") || Language(c, "ja")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.indian", "Indian / Bollywood", "Country & Language", order++, "\uE774", "Indian and Bollywood naming", c => Any(c, "indian", "india", "bollywood", "hindi") || Language(c, "hi")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.arabic", "Arabic Movies", "Country & Language", order++, "\uE774", "Arabic movie naming", c => Any(c, "arabic", "arab") || Language(c, "ar")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.latin", "Latin Movies", "Country & Language", order++, "\uE774", "Latin American movie naming", c => Any(c, "latin", "latino", "latam", "mexico", "brazil", "argentina")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.french", "French Movies", "Country & Language", order++, "\uE774", "French movie naming", c => Any(c, "french", "france") || Language(c, "fr")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.german", "German Movies", "Country & Language", order++, "\uE774", "German movie naming", c => Any(c, "german", "germany", "deutsch") || Language(c, "de")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.spanish", "Spanish Movies", "Country & Language", order++, "\uE774", "Spanish movie naming", c => Any(c, "spanish", "spain") || Language(c, "es")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.italian", "Italian Movies", "Country & Language", order++, "\uE774", "Italian movie naming", c => Any(c, "italian", "italy") || Language(c, "it")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.russian", "Russian Movies", "Country & Language", order++, "\uE774", "Russian movie naming", c => Any(c, "russian", "russia") || Language(c, "ru")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.world", "World Cinema", "Country & Language", order++, "\uE774", "foreign, world, festival, or non-English naming", c => Any(c, "world cinema", "foreign", "international", "festival") || (!string.IsNullOrWhiteSpace(c.OriginalLanguage) && !Language(c, "en") && !Language(c, "tr"))));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.dubbed_tr", "Dubbed Turkish", "Country & Language", order++, "\uE774", "Turkish dub naming", c => Any(c, "dubbed turkish", "turkish dubbed", "dublaj", "tr dublaj")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.country.subtitled", "Subtitled", "Country & Language", order++, "\uE774", "subtitle naming", c => Any(c, "subtitled", "subtitle", "sub", "altyazi", "alt yazili")));
        }

        private static void AddMoviePlatforms(List<SmartCategoryDefinition> definitions, ref int order)
        {
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Netflix", "netflix");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Prime Video", "prime video", "amazon prime", "amazon");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Disney+", "disney+", "disney plus", "disney");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "HBO / Max", "hbo", "max");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Apple TV+", "apple tv", "apple tv+", "appletv");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "BluTV", "blutv", "blu tv");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Exxen", "exxen");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Gain", "gain");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "TOD / beIN Connect", "tod", "bein connect");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "MUBI / Festival Films", "mubi", "festival");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Marvel", "marvel", "mcu");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "DC", "dc", "dc universe");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Pixar", "pixar");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "DreamWorks", "dreamworks", "dream works");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Warner Bros.", "warner bros", "warner");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Universal", "universal");
            AddPlatform(definitions, SmartCategoryMediaType.Movie, ref order, "Paramount", "paramount", "paramount+");
        }

        private static void AddSeriesPlatforms(List<SmartCategoryDefinition> definitions, ref int order)
        {
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Netflix Series", "netflix");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Prime Video Series", "prime video", "amazon prime", "amazon");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Disney+ Series", "disney+", "disney plus", "disney");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "HBO / Max Series", "hbo", "max");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Apple TV+ Series", "apple tv", "apple tv+", "appletv");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "BluTV Series", "blutv", "blu tv");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Exxen Series", "exxen");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Gain Series", "gain");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Tabii Series", "tabii");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "TRT Digital", "trt digital");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "TOD / beIN Connect Series", "tod", "bein connect");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Paramount+ Series", "paramount", "paramount+");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Hulu Series", "hulu");
            AddPlatform(definitions, SmartCategoryMediaType.Series, ref order, "Peacock Series", "peacock");
        }

        private static void AddPlatform(List<SmartCategoryDefinition> definitions, SmartCategoryMediaType mediaType, ref int order, string name, params string[] phrases)
        {
            var prefix = mediaType == SmartCategoryMediaType.Movie ? "movie.platform" : "series.platform";
            definitions.Add(Category(mediaType, $"{prefix}.{NormalizeForMatching(name).Replace(' ', '_')}", name, "Platforms / Studios", order++, "\uE71D", $"{name} platform, studio, or collection naming", c => Any(c, phrases)));
        }

        private static void AddMovieCollections(List<SmartCategoryDefinition> definitions, ref int order)
        {
            AddCollection(definitions, ref order, "James Bond Collection", "james bond", "007", "bond");
            AddCollection(definitions, ref order, "Marvel Cinematic Universe", "marvel cinematic universe", "mcu", "avengers", "iron man", "captain america", "thor");
            AddCollection(definitions, ref order, "DC Universe", "dc universe", "justice league", "superman", "wonder woman", "aquaman");
            AddCollection(definitions, ref order, "Harry Potter / Wizarding World", "harry potter", "wizarding world", "fantastic beasts");
            AddCollection(definitions, ref order, "Lord of the Rings", "lord of the rings", "lotr", "hobbit");
            AddCollection(definitions, ref order, "Star Wars", "star wars", "mandalorian");
            AddCollection(definitions, ref order, "Fast & Furious", "fast furious", "fast and furious");
            AddCollection(definitions, ref order, "Mission: Impossible", "mission impossible");
            AddCollection(definitions, ref order, "John Wick", "john wick");
            AddCollection(definitions, ref order, "The Matrix", "matrix");
            AddCollection(definitions, ref order, "Jurassic Park / World", "jurassic park", "jurassic world");
            AddCollection(definitions, ref order, "Transformers", "transformers");
            AddCollection(definitions, ref order, "Pirates of the Caribbean", "pirates of the caribbean");
            AddCollection(definitions, ref order, "Godzilla / MonsterVerse", "godzilla", "monsterverse", "kong skull island");
            AddCollection(definitions, ref order, "Batman Collection", "batman", "dark knight");
            AddCollection(definitions, ref order, "Spider-Man Collection", "spider man", "spiderman", "spider verse");
            AddCollection(definitions, ref order, "X-Men Collection", "x men", "xmen", "wolverine");
            AddCollection(definitions, ref order, "Alien / Predator", "alien", "predator");
            AddCollection(definitions, ref order, "Rocky / Creed", "rocky", "creed");
            AddCollection(definitions, ref order, "Saw Collection", "saw");
            AddCollection(definitions, ref order, "Scream Collection", "scream");
        }

        private static void AddCollection(List<SmartCategoryDefinition> definitions, ref int order, string name, params string[] phrases)
        {
            definitions.Add(Category(SmartCategoryMediaType.Movie, $"movie.collection.{NormalizeForMatching(name).Replace(' ', '_')}", name, "Collections", order++, "\uE8FD", $"{name} title or group naming", c => Any(c, phrases)));
        }

        private static void AddMovieYearsAndQuality(List<SmartCategoryDefinition> definitions, ref int order)
        {
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.year.2020s", "2020s", "Year / Era", order++, "\uE787", "release year 2020-2029", c => Year(c) is >= 2020 and <= 2029));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.year.2010s", "2010s", "Year / Era", order++, "\uE787", "release year 2010-2019", c => Year(c) is >= 2010 and <= 2019));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.year.2000s", "2000s", "Year / Era", order++, "\uE787", "release year 2000-2009", c => Year(c) is >= 2000 and <= 2009));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.year.1990s", "1990s", "Year / Era", order++, "\uE787", "release year 1990-1999", c => Year(c) is >= 1990 and <= 1999));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.year.1980s", "1980s", "Year / Era", order++, "\uE787", "release year 1980-1989", c => Year(c) is >= 1980 and <= 1989));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.year.classic", "Classic Movies", "Year / Era", order++, "\uE787", "release before 1980", c => Year(c) is > 0 and < 1980));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.year.new_releases", "New Releases", "Year / Era", order++, "\uE823", "release in the last two years or new naming", c => Year(c) >= DateTime.UtcNow.Year - 1 || Any(c, "new release", "new movies", "new movie")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.year.old_but_gold", "Old but Gold", "Year / Era", order++, "\uE734", "classic high-rated movies", c => Year(c) is > 0 and < 2000 && c.VoteAverage >= 7));

            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.4k", "4K / UHD", "Quality / Technical", order++, "\uE7F4", "4K or UHD naming", c => Any(c, "4k", "uhd", "ultra hd")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.1080p", "1080p Full HD", "Quality / Technical", order++, "\uE7F4", "1080p or FHD naming", c => Any(c, "1080p", "1080", "full hd", "fhd")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.720p", "720p HD", "Quality / Technical", order++, "\uE7F4", "720p or HD naming", c => Any(c, "720p", "720", " hd ")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.hdr", "HDR", "Quality / Technical", order++, "\uE7F4", "HDR, Dolby Vision, or DV naming", c => Any(c, "hdr", "dolby vision", "dv")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.dolby", "Dolby / 5.1", "Quality / Technical", order++, "\uE7F4", "Dolby, Atmos, DTS, or 5.1 naming", c => Any(c, "dolby", "atmos", "dts", "5 1", "7 1")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.high_bitrate", "High Bitrate", "Quality / Technical", order++, "\uE7F4", "remux, bluray, or high-bitrate naming", c => Any(c, "remux", "blu ray", "bluray", "bdrip", "high bitrate")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.low_bandwidth", "Low Bandwidth Friendly", "Quality / Technical", order++, "\uE7F4", "SD, mobile, or low-bandwidth naming", c => Any(c, "sd", "mobile", "low bandwidth", "480p")));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.duplicates", "Duplicate Titles", "Quality / Technical", order++, "\uE8B2", "deduplicated movie groups with multiple variants", c => c.VariantCount > 1));
            definitions.Add(Category(SmartCategoryMediaType.Movie, "movie.quality.missing_poster", "Missing Poster", "Quality / Technical", order++, "\uEB9F", "poster is missing", c => !c.HasArtwork));
        }

        private static void AddTurkishSeries(List<SmartCategoryDefinition> definitions, ref int order)
        {
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.all", "Turkish Series", "Turkish Series", order++, "\uE8A9", "Turkish series naming", IsTurkish));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.drama", "Turkish Drama", "Turkish Series", order++, "\uE8A9", "Turkish drama naming", c => IsTurkish(c) && Any(c, "drama", "dram")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.comedy", "Turkish Comedy", "Turkish Series", order++, "\uE8A9", "Turkish comedy naming", c => IsTurkish(c) && Any(c, "comedy", "komedi")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.crime", "Turkish Crime", "Turkish Series", order++, "\uE8A9", "Turkish crime naming", c => IsTurkish(c) && Any(c, "crime", "suc", "polisiye")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.historical", "Turkish Historical", "Turkish Series", order++, "\uE8A9", "Turkish historical naming", c => IsTurkish(c) && Any(c, "historical", "tarihi", "osmanli")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.romance", "Turkish Romance", "Turkish Series", order++, "\uE8A9", "Turkish romance naming", c => IsTurkish(c) && Any(c, "romance", "romantik", "ask")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.family", "Turkish Family", "Turkish Series", order++, "\uE8A9", "Turkish family naming", c => IsTurkish(c) && Any(c, "family", "aile")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.daily", "Turkish Daily Series", "Turkish Series", order++, "\uE8A9", "Turkish daily series naming", c => IsTurkish(c) && Any(c, "daily", "gunluk", "haftalik")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.finalized", "Turkish Finalized Series", "Turkish Series", order++, "\uE8A9", "Turkish final or completed provider naming", c => IsTurkish(c) && Any(c, "final", "finalized", "completed", "bitti", "tv final")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.tr.ongoing", "Turkish Ongoing Series", "Turkish Series", order++, "\uE8A9", "Turkish ongoing provider naming", c => IsTurkish(c) && Any(c, "ongoing", "devam", "yeni bolum", "guncel")));
            AddTrChannel(definitions, ref order, "TR / Kanal D", "kanal d");
            AddTrChannel(definitions, ref order, "TR / ATV", "atv");
            AddTrChannel(definitions, ref order, "TR / Show TV", "show tv");
            AddTrChannel(definitions, ref order, "TR / Star TV", "star tv");
            AddTrChannel(definitions, ref order, "TR / TRT 1", "trt 1", "trt1");
            AddTrChannel(definitions, ref order, "TR / NOW / FOX", "now", "fox");
            AddTrChannel(definitions, ref order, "TR / TV8", "tv8");
        }

        private static void AddTrChannel(List<SmartCategoryDefinition> definitions, ref int order, string name, params string[] phrases)
        {
            definitions.Add(Category(SmartCategoryMediaType.Series, $"series.tr.channel.{NormalizeForMatching(name).Replace(' ', '_')}", name, "Turkish Series", order++, "\uE8A9", $"{name} provider naming", c => IsTurkish(c) && Any(c, phrases)));
        }

        private static void AddSeriesCountries(List<SmartCategoryDefinition> definitions, ref int order)
        {
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.turkish", "Turkish", "Country & Language", order++, "\uE774", "Turkish language or provider naming", IsTurkish));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.american", "American", "Country & Language", order++, "\uE774", "US or English-language naming", c => Any(c, "american", "usa", "us series") || Language(c, "en")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.british", "British", "Country & Language", order++, "\uE774", "British series naming", c => Any(c, "british", "uk", "bbc", "itv")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.korean", "Korean", "Country & Language", order++, "\uE774", "Korean series naming", c => Any(c, "korean", "k drama", "kdrama") || Language(c, "ko")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.japanese", "Japanese", "Country & Language", order++, "\uE774", "Japanese series naming", c => Any(c, "japanese", "japan") || Language(c, "ja")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.spanish", "Spanish", "Country & Language", order++, "\uE774", "Spanish series naming", c => Any(c, "spanish", "spain") || Language(c, "es")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.german", "German", "Country & Language", order++, "\uE774", "German series naming", c => Any(c, "german", "germany") || Language(c, "de")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.french", "French", "Country & Language", order++, "\uE774", "French series naming", c => Any(c, "french", "france") || Language(c, "fr")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.italian", "Italian", "Country & Language", order++, "\uE774", "Italian series naming", c => Any(c, "italian", "italy") || Language(c, "it")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.nordic", "Nordic", "Country & Language", order++, "\uE774", "Nordic series naming", c => Any(c, "nordic", "swedish", "norwegian", "danish", "finnish", "icelandic")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.arabic", "Arabic", "Country & Language", order++, "\uE774", "Arabic series naming", c => Any(c, "arabic", "arab") || Language(c, "ar")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.indian", "Indian", "Country & Language", order++, "\uE774", "Indian series naming", c => Any(c, "indian", "india", "hindi") || Language(c, "hi")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.latin", "Latin American", "Country & Language", order++, "\uE774", "Latin American series naming", c => Any(c, "latin", "latino", "latam", "mexico", "brazil", "argentina")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.anime", "Anime / Japanese Animation", "Country & Language", order++, "\uE774", "anime or Japanese animation naming", c => Any(c, "anime", "japanese animation")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.country.world", "World Series", "Country & Language", order++, "\uE774", "foreign, world, or international naming", c => Any(c, "world", "foreign", "international") || (!string.IsNullOrWhiteSpace(c.OriginalLanguage) && !Language(c, "en") && !Language(c, "tr"))));
        }

        private static void AddSeriesStatus(List<SmartCategoryDefinition> definitions, ref int order)
        {
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.ongoing", "Ongoing Shows", "Status / Seasons", order++, "\uE787", "ongoing provider naming", c => Any(c, "ongoing", "devam", "new episode", "yeni bolum", "guncel")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.finalized", "Finalized Shows", "Status / Seasons", order++, "\uE73E", "final or completed provider naming", c => Any(c, "final", "finalized", "completed", "complete", "bitti", "tv final")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.mini", "Mini Series", "Status / Seasons", order++, "\uE8A9", "mini-series naming", c => Any(c, "mini series", "miniseries", "limited series")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.one_season", "One Season Shows", "Status / Seasons", order++, "\uE8A9", "single season data or naming", c => c.SeasonCount == 1 || Any(c, "season 1", "s01")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.long_running", "Long Running Shows", "Status / Seasons", order++, "\uE8A9", "five or more seasons", c => c.SeasonCount >= 5));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.new_season", "New Season Added", "Status / Seasons", order++, "\uE823", "new season naming", c => Any(c, "new season", "season added", "yeni sezon")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.recently_updated", "Recently Updated Episodes", "Status / Seasons", order++, "\uE823", "recent source sync or updated naming", c => IsRecentlyAdded(c) || Any(c, "updated", "guncel", "new episode", "yeni bolum")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.missing_episodes", "Missing Episodes", "Status / Seasons", order++, "\uE711", "missing episode state or naming", c => c.HasMissingEpisodes || Any(c, "missing episode", "eksik bolum")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.complete_seasons", "Complete Seasons", "Status / Seasons", order++, "\uE73E", "complete season state or naming", c => c.HasCompleteSeasons || Any(c, "complete season", "full season", "sezon tamam")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.season_1", "Season 1 Available", "Status / Seasons", order++, "\uE8A9", "season 1 data or naming", c => c.SeasonCount >= 1 || Any(c, "season 1", "s01")));
            definitions.Add(Category(SmartCategoryMediaType.Series, "series.status.multi_season", "Multi-Season Shows", "Status / Seasons", order++, "\uE8A9", "two or more seasons", c => c.SeasonCount >= 2 || Any(c, "multi season", "seasons")));
        }

        private static SmartCategoryDefinition Category(
            SmartCategoryMediaType mediaType,
            string id,
            string displayName,
            string sectionName,
            int order,
            string iconGlyph,
            string matchRule,
            Func<SmartCategoryItemContext, bool> predicate,
            bool alwaysShow = false,
            bool isAll = false)
        {
            return new SmartCategoryDefinition
            {
                Id = id,
                DisplayName = displayName,
                SectionName = sectionName,
                MediaType = mediaType,
                IconGlyph = iconGlyph,
                MatchRule = matchRule,
                SortPriority = order,
                AlwaysShow = alwaysShow,
                IsAllCategory = isAll,
                Predicate = predicate
            };
        }

        private static bool IsSports(SmartCategoryItemContext context)
        {
            return Any(context, "sports", "sport", "spor", "bein sports", "s sport", "football", "soccer", "futbol", "basketball", "basketbol", "tennis", "golf", "motorsport", "formula 1", "nba", "euroleague", "espn", "eurosport");
        }

        private static bool IsFootballLeague(SmartCategoryItemContext context)
        {
            return Any(context, "super lig", "premier league", "la liga", "serie a", "bundesliga", "ligue 1", "champions league", "europa league", "conference league", "mls", "eredivisie", "libertadores");
        }

        private static bool IsHighQuality(SmartCategoryItemContext context)
        {
            return Any(context, "4k", "uhd", "ultra hd", "1080p", "1080", "fhd", "full hd", "720p", "720", "hd");
        }

        private static bool IsInternational(SmartCategoryItemContext context)
        {
            return Any(context, "international", "world", "foreign", "english", "arabic", "german", "french", "italian", "spanish", "russian", "korean", "japanese", "balkan", "latin");
        }

        private static bool IsAdult(SmartCategoryItemContext context)
        {
            return Any(context, "adult", "18+", "18 plus", "xxx", "porn", "porno", "erotic");
        }

        private static bool IsTurkish(SmartCategoryItemContext context)
        {
            return Language(context, "tr") || Any(context, "turkish", "turkiye", "turk", "tr dizi", "tr film", "yerli", "kanal d", "show tv", "atv", "star tv", "trt", "tv8", "now tv", "fox tv");
        }

        private static bool IsRecentlyAdded(SmartCategoryItemContext context)
        {
            return context.SourceLastSyncUtc.HasValue &&
                   context.SourceLastSyncUtc.Value >= DateTime.UtcNow.AddDays(-14);
        }

        private static bool Language(SmartCategoryItemContext context, string languageCode)
        {
            return string.Equals(context.OriginalLanguage?.Trim(), languageCode, StringComparison.OrdinalIgnoreCase);
        }

        private static int Year(SmartCategoryItemContext context)
        {
            if (context.ReleaseDate.HasValue)
            {
                return context.ReleaseDate.Value.Year;
            }

            var match = YearRegex.Match($"{context.Title} {context.RawTitle} {context.ProviderGroupName}");
            return match.Success && int.TryParse(match.Value, out var year) ? year : 0;
        }

        private static bool Any(SmartCategoryItemContext context, params string[] phrases)
        {
            return ContainsAny(BuildSearchText(context), phrases);
        }

        private static bool Any(string value, params string[] phrases)
        {
            return ContainsAny(value, phrases);
        }

        private static string GuideText(SmartCategoryItemContext context)
        {
            return $"{context.EpgCurrentTitle} {context.EpgNextTitle}";
        }

        private static string BuildSearchText(SmartCategoryItemContext context)
        {
            return string.Join(
                ' ',
                context.Title,
                context.RawTitle,
                context.ProviderGroupName,
                context.DisplayCategoryName,
                context.Genres,
                context.SourceName,
                context.SourceSummary,
                context.TvgId,
                context.TvgName,
                context.LogoUrl,
                context.EpgCurrentTitle,
                context.EpgNextTitle,
                context.OriginalLanguage);
        }

        private static bool ContainsAny(string value, IEnumerable<string> phrases)
        {
            var normalized = $" {NormalizeForMatching(value)} ";
            foreach (var phrase in phrases)
            {
                var normalizedPhrase = NormalizeForMatching(phrase);
                if (string.IsNullOrWhiteSpace(normalizedPhrase))
                {
                    continue;
                }

                if (normalized.Contains($" {normalizedPhrase} ", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeForMatching(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = RemoveDiacritics(ContentClassifier.NormalizeLabel(value)).ToLowerInvariant();
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            return MultiWhitespaceRegex.Replace(builder.ToString(), " ").Trim();
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
    }
}

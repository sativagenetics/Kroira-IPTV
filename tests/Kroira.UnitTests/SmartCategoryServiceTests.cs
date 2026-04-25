#nullable enable
using System.Linq;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests
{
    [TestClass]
    public class SmartCategoryServiceTests
    {
        private readonly SmartCategoryService _service = new();

        [TestMethod]
        public void LiveSportsLeagueMatchesSportsFootballAndSuperLig()
        {
            var context = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Live,
                Title = "beIN SPORTS 1 HD",
                ProviderGroupName = "TR Sports / Super Lig",
                EpgCurrentTitle = "Galatasaray v Fenerbahce - Super Lig Live",
                HasGuideLink = true
            };

            Assert.IsTrue(_service.Matches("live.sports.all", context));
            Assert.IsTrue(_service.Matches("live.sports.football", context));
            Assert.IsTrue(_service.Matches("live.football.super_lig", context));
        }

        [TestMethod]
        public void LiveBasketballNamesMatchBasketballBuckets()
        {
            var nba = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Live,
                Title = "NBA TV",
                ProviderGroupName = "US Sports"
            };
            var euroLeague = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Live,
                Title = "S Sport EuroLeague",
                ProviderGroupName = "Basketball"
            };

            Assert.IsTrue(_service.Matches("live.sports.basketball", nba));
            Assert.IsTrue(_service.Matches("live.basketball.nba", nba));
            Assert.IsTrue(_service.Matches("live.sports.basketball", euroLeague));
            Assert.IsTrue(_service.Matches("live.basketball.euroleague", euroLeague));
        }

        [TestMethod]
        public void MovieCollectionsAndQualityUseTitleAndGroupHints()
        {
            var bond = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Movie,
                Title = "007 Skyfall",
                ProviderGroupName = "James Bond Collection"
            };
            var uhd = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Movie,
                Title = "Dune Part Two 4K UHD HDR",
                ProviderGroupName = "New Movies"
            };

            Assert.IsTrue(_service.Matches("movie.collection.james_bond_collection", bond));
            Assert.IsTrue(_service.Matches("movie.quality.4k", uhd));
            Assert.IsTrue(_service.Matches("movie.quality.hdr", uhd));
        }

        [TestMethod]
        public void PlatformGroupsMatchMoviesAndSeries()
        {
            var netflixMovie = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Movie,
                Title = "The Killer",
                ProviderGroupName = "Netflix Movies"
            };
            var appleSeries = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Series,
                Title = "Severance",
                ProviderGroupName = "Apple TV+ Series"
            };
            var disneySeries = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Series,
                Title = "Loki",
                ProviderGroupName = "Disney Plus Series"
            };

            Assert.IsTrue(_service.Matches("movie.platform.netflix", netflixMovie));
            Assert.IsTrue(MatchesDisplayName(SmartCategoryMediaType.Series, "Apple TV+ Series", appleSeries));
            Assert.IsTrue(MatchesDisplayName(SmartCategoryMediaType.Series, "Disney+ Series", disneySeries));
        }

        [TestMethod]
        public void TurkishFinalSeriesGroupMapsToTurkishFinalizedSeries()
        {
            var context = new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Series,
                Title = "Yargi",
                ProviderGroupName = "Tr/Dizi / TV Final",
                OriginalLanguage = "tr"
            };

            Assert.IsTrue(_service.Matches("series.tr.all", context));
            Assert.IsTrue(_service.Matches("series.tr.finalized", context));
        }

        [TestMethod]
        public void OriginalProviderGroupFallbackKeysAreStable()
        {
            var key = _service.BuildOriginalProviderGroupKey("Tr/Dizi / Apple");

            Assert.IsTrue(key.StartsWith("provider:"));
            Assert.IsTrue(_service.TryParseOriginalProviderGroupKey(key, out var normalized));
            Assert.AreEqual(_service.NormalizeKey("Tr/Dizi / Apple"), normalized);
        }

        [TestMethod]
        public void SmartCategoryIndexReturnsSmartAndProviderMemberships()
        {
            var items = new[]
            {
                new SmartCategoryIndexTestItem(1, "007 Skyfall", "James Bond Collection", false, "Netflix"),
                new SmartCategoryIndexTestItem(2, "Dune Part Two 4K UHD", "New Movies", false, "Prime Video")
            };

            var index = BuildMovieIndex(items);

            CollectionAssert.Contains(index.GetItems("movie.collection.james_bond_collection").Select(item => item.Id).ToList(), 1);
            CollectionAssert.Contains(index.GetItems("movie.quality.4k").Select(item => item.Id).ToList(), 2);
            Assert.AreEqual(2, index.GetCount(string.Empty));
            Assert.AreEqual(2, index.ContextBuildCount);

            var providerKey = _service.BuildOriginalProviderGroupKey("James Bond Collection");
            CollectionAssert.Contains(index.GetItems(providerKey).Select(item => item.Id).ToList(), 1);
            Assert.IsTrue(index.OriginalProviderGroups.Any(group => group.Key == providerKey && group.Count == 1));
        }

        [TestMethod]
        public void SmartCategoryIndexCountsAreStableAcrossRepeatedSelection()
        {
            var items = new[]
            {
                new SmartCategoryIndexTestItem(1, "The Killer", "Netflix Movies", true, "Netflix"),
                new SmartCategoryIndexTestItem(2, "Severance", "Apple TV+ Movies", false, "Apple")
            };
            var index = BuildMovieIndex(items);
            var favoritesBefore = index.GetCount("movie.library.favorites");

            _ = index.GetItems("movie.platform.netflix");
            _ = index.GetItems("movie.platform.apple_tv");
            _ = index.GetItems("movie.platform.netflix");

            Assert.AreEqual(favoritesBefore, index.GetCount("movie.library.favorites"));
            Assert.AreEqual(1, favoritesBefore);
        }

        [TestMethod]
        public void SmartCategoryIndexCandidateSetComposesWithProviderSearchAndFavorites()
        {
            var items = new[]
            {
                new SmartCategoryIndexTestItem(1, "The Killer", "Netflix Movies", true, "Netflix"),
                new SmartCategoryIndexTestItem(2, "The Crown", "Netflix Movies", false, "Netflix"),
                new SmartCategoryIndexTestItem(3, "Ted Lasso", "Apple TV+ Movies", true, "Apple")
            };
            var index = BuildMovieIndex(items);
            var providerKey = _service.BuildOriginalProviderGroupKey("Netflix Movies");
            var normalizedSearch = _service.NormalizeKey("killer");

            var composed = index.GetItems(providerKey)
                .Where(item => item.SourceName == "Netflix")
                .Where(item => item.IsFavorite)
                .Where(item => _service.NormalizeKey(item.Title).Contains(normalizedSearch))
                .Select(item => item.Id)
                .ToList();

            CollectionAssert.AreEqual(new[] { 1 }, composed);
            Assert.AreEqual(2, index.GetCount(providerKey));
        }

        private bool MatchesDisplayName(
            SmartCategoryMediaType mediaType,
            string displayName,
            SmartCategoryItemContext context)
        {
            var definition = _service.GetDefinitions(mediaType)
                .Single(item => item.DisplayName == displayName);
            return _service.Matches(definition.Id, context);
        }

        private SmartCategoryIndex<SmartCategoryIndexTestItem> BuildMovieIndex(SmartCategoryIndexTestItem[] items)
        {
            return SmartCategoryIndexBuilder.Build(
                items,
                _service.GetDefinitions(SmartCategoryMediaType.Movie),
                item => new SmartCategoryItemContext
                {
                    MediaType = SmartCategoryMediaType.Movie,
                    Title = item.Title,
                    ProviderGroupName = item.ProviderGroup,
                    SourceName = item.SourceName,
                    SourceSummary = item.SourceName,
                    IsFavorite = item.IsFavorite
                },
                item => new[] { item.ProviderGroup },
                _service);
        }

        private sealed record SmartCategoryIndexTestItem(
            int Id,
            string Title,
            string ProviderGroup,
            bool IsFavorite,
            string SourceName);
    }
}

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

        private bool MatchesDisplayName(
            SmartCategoryMediaType mediaType,
            string displayName,
            SmartCategoryItemContext context)
        {
            var definition = _service.GetDefinitions(mediaType)
                .Single(item => item.DisplayName == displayName);
            return _service.Matches(definition.Id, context);
        }
    }
}

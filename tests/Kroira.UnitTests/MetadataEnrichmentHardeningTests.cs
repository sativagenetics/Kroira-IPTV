using System.Linq;
using Kroira.App.Services.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class MetadataEnrichmentHardeningTests
{
    [TestMethod]
    public void AnalyzeMovie_CleansProviderNoiseAndKeepsYear()
    {
        var analysis = MetadataTitleAnalyzer.AnalyzeMovie("TR: The.Matrix.1999.1080p.WEB-DL.x264");

        Assert.AreEqual("The Matrix", analysis.CleanTitle);
        Assert.AreEqual("the matrix", analysis.NormalizedTitle);
        Assert.AreEqual(1999, analysis.Year);
        Assert.IsTrue(analysis.SearchTitles.Contains("The Matrix"));
    }

    [TestMethod]
    public void AnalyzeMovie_ExtractsYearFromNoisyTitle()
    {
        var analysis = MetadataTitleAnalyzer.AnalyzeMovie("Dune Part Two (2024) 4K HDR10 WEBRip");

        Assert.AreEqual("Dune Part Two", analysis.CleanTitle);
        Assert.AreEqual(2024, analysis.Year);
    }

    [TestMethod]
    public void AnalyzeSeries_RecognizesSeasonEpisodeSyntax()
    {
        var analysis = MetadataTitleAnalyzer.AnalyzeSeries("Some.Show.S02E05.1080p.WEB-DL");

        Assert.AreEqual("Some Show", analysis.CleanTitle);
        Assert.AreEqual("some show", analysis.NormalizedTitle);
        Assert.AreEqual(2, analysis.SeasonNumber);
        Assert.AreEqual(5, analysis.EpisodeNumber);
        Assert.IsTrue(analysis.LooksLikeEpisode);
    }

    [TestMethod]
    public void AnalyzeSeries_RecognizesLocalizedSeasonEpisodeSyntax()
    {
        var analysis = MetadataTitleAnalyzer.AnalyzeSeries("Great Show - Sezon 2 Bolum 5 (2021) HD");

        Assert.AreEqual("Great Show", analysis.CleanTitle);
        Assert.AreEqual(2021, analysis.Year);
        Assert.AreEqual(2, analysis.SeasonNumber);
        Assert.AreEqual(5, analysis.EpisodeNumber);
    }

    [TestMethod]
    public void ScoreCandidate_PrefersExactTitleAndYear()
    {
        var exact = MetadataTitleAnalyzer.ScoreCandidate(
            "The Matrix",
            1999,
            new MetadataCandidate("1", "The Matrix", "The Matrix", 1999, 80, 5000),
            MetadataMediaKind.Movie);

        var weaker = MetadataTitleAnalyzer.ScoreCandidate(
            "The Matrix",
            1999,
            new MetadataCandidate("2", "Matrix Reloaded", "The Matrix Reloaded", 2003, 95, 5000),
            MetadataMediaKind.Movie);

        Assert.IsTrue(exact.IsAcceptable);
        Assert.IsTrue(exact.Score > weaker.Score);
        Assert.IsFalse(weaker.IsAcceptable);
    }

    [TestMethod]
    public void PickBestCandidate_ReturnsNullForWeakNoMatch()
    {
        var best = MetadataTitleAnalyzer.PickBestCandidate(
            "The Matrix",
            1999,
            new[]
            {
                new MetadataCandidate("9", "Cooking With Friends", "Cooking With Friends", 1999, 100, 10000)
            },
            MetadataMediaKind.Movie);

        Assert.IsNull(best);
    }
}

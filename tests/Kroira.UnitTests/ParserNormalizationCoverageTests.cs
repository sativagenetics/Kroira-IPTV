using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Parsing;
using Kroira.UnitTests.Fixtures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class ParserNormalizationCoverageTests
{
    [TestMethod]
    public void M3uExtinf_ParsesEscapedQuotesUnquotedAttributesAndMissingComma()
    {
        var metadata = M3uMetadataParser.ParseExtinf(SyntheticSourceFixtures.M3uEscapedQuoteMissingCommaLine);

        Assert.AreEqual("News Fixture", metadata.DisplayName);
        Assert.AreEqual("fixture.one", metadata.Attributes["tvg-id"]);
        Assert.AreEqual("*** News |", metadata.Attributes["group-title"]);
        Assert.AreEqual("https://img.example/logo.png?size=small&token=safe", metadata.Attributes["tvg-logo"]);
        Assert.AreEqual("Fixture \"Quoted\" Channel", metadata.Attributes["tvg-name"]);
    }

    [TestMethod]
    public void M3uHeader_HandlesBomCommentsUnusualCasingAndRelativeGuideUrl()
    {
        var metadata = M3uMetadataParser.ParseHeaderMetadata(
            SyntheticSourceFixtures.M3uHeaderWithBomCommentsAndRelativeGuide,
            "https://playlist.example/path/source.m3u");

        CollectionAssert.AreEquivalent(
            new[]
            {
                "https://guide.example/a.xml?token=safe&x=1",
                "https://playlist.example/path/relative-guide.xml"
            },
            metadata.XmltvUrls.ToArray());
        Assert.IsTrue(metadata.Attributes.ContainsKey("XMLTV"));
        Assert.IsTrue(metadata.HeaderLines.Any(line => line.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ContentClassifier_ClassifiesConservativelyAcrossM3uEntryKinds()
    {
        Assert.AreEqual(
            ContentClassifier.M3uEntryType.Live,
            ContentClassifier.ClassifyM3uEntry(
                "World News",
                "https://stream.example/live/world-news.ts?token=safe",
                "News",
                string.Empty));

        Assert.AreEqual(
            ContentClassifier.M3uEntryType.Movie,
            ContentClassifier.ClassifyM3uEntry(
                "Standalone Show",
                "https://stream.example/series/user/pass/100.mp4?token=safe",
                "Series",
                string.Empty));

        Assert.AreEqual(
            ContentClassifier.M3uEntryType.Episode,
            ContentClassifier.ClassifyM3uEntry(
                "Standalone Show S01E02 Next",
                "https://stream.example/series/user/pass/101.mp4?token=safe",
                "Series",
                string.Empty));

        Assert.AreEqual(
            ContentClassifier.M3uEntryType.Radio,
            ContentClassifier.ClassifyM3uEntry(
                "Rock Radio",
                "https://stream.example/radio/rock.mp3?token=safe",
                "Radio",
                string.Empty));
    }

    [TestMethod]
    public void CatalogNormalization_StripsProviderNoiseWithoutLosingRealCategory()
    {
        var normalized = new CatalogNormalizationService().NormalizeMovie(
            SourceType.M3U,
            "TR: Example Movie 1080p WEB-DL",
            "Provider Movies - Action HD");

        Assert.AreEqual("Example Movie", normalized.Title);
        Assert.AreEqual("Action", normalized.CategoryName);
        Assert.AreEqual("Primary", normalized.ContentKind);
        Assert.AreEqual("TR: Example Movie 1080p WEB-DL", normalized.RawTitle);
    }
}

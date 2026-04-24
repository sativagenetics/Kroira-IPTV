using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class CatalogFingerprintingSmokeTests
{
    [TestMethod]
    public void ComputeMovie_UsesTmdbIdAsStrongFingerprint()
    {
        var movie = new Movie
        {
            SourceProfileId = 12,
            Title = "  Le Fabuleux Destin d'Amelie Poulain  ",
            ContentKind = "Primary",
            TmdbId = "tmdb:194",
            StreamUrl = "https://example.invalid/movie/user/pass/194.mp4"
        };

        var result = CatalogFingerprinting.ComputeMovie(movie);

        Assert.AreEqual("le fabuleux destin d amelie poulain", result.CanonicalTitleKey);
        Assert.AreEqual("movie:tmdb:Primary:194", result.DedupFingerprint);
        Assert.IsTrue(result.IsStrong);
    }

    [TestMethod]
    public void ComputeSeries_FallsBackToSourceExternalIdWhenMetadataIsAbsent()
    {
        var series = new Series
        {
            SourceProfileId = 7,
            Title = "Example Show",
            ContentKind = "",
            ExternalId = " series-42 "
        };

        var result = CatalogFingerprinting.ComputeSeries(series);

        Assert.AreEqual("example show", result.CanonicalTitleKey);
        Assert.AreEqual("series:source:Primary:7:ext:series-42", result.DedupFingerprint);
        Assert.IsFalse(result.IsStrong);
    }
}

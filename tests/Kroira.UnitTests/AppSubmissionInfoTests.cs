using Kroira.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class AppSubmissionInfoTests
{
    [TestMethod]
    public void StoreMetadata_HasRequiredVersionDescriptionsAndLinks()
    {
        Assert.AreEqual("KROIRA IPTV", AppSubmissionInfo.AppName);
        Assert.AreEqual("2.0.0", AppSubmissionInfo.ReleaseVersion);

        AssertContains(AppSubmissionInfo.ShortDescription, "media player");
        AssertContains(AppSubmissionInfo.ShortDescription, "source manager");
        AssertContains(AppSubmissionInfo.LongDescription, "M3U");
        AssertContains(AppSubmissionInfo.LongDescription, "Xtream");
        AssertContains(AppSubmissionInfo.LongDescription, "supported provider portal profiles");
        AssertContains(AppSubmissionInfo.LongDescription, "does not provide");

        Assert.IsTrue(AppSubmissionInfo.TryCreatePrivacyPolicyUri(out var privacyUri));
        Assert.AreEqual("https", privacyUri!.Scheme);
        Assert.IsTrue(AppSubmissionInfo.TryCreateSupportPageUri(out var supportUri));
        Assert.AreEqual("https", supportUri!.Scheme);
        Assert.IsTrue(AppSubmissionInfo.TryCreateSupportEmailUri(out var supportEmailUri));
        Assert.AreEqual("mailto", supportEmailUri!.Scheme);
    }

    [TestMethod]
    public void Disclaimer_CoversNoContentAuthorizationAndNoBypass()
    {
        var disclaimer = AppSubmissionInfo.LegalDisclaimer;

        AssertContains(disclaimer, "media player and source manager");
        AssertContains(disclaimer, "does not provide");
        AssertContains(disclaimer, "channels");
        AssertContains(disclaimer, "streams");
        AssertContains(disclaimer, "playlists");
        AssertContains(disclaimer, "subscriptions");
        AssertContains(disclaimer, "media content");
        AssertContains(disclaimer, "authorized sources");
        AssertContains(disclaimer, "does not bypass");
        AssertContains(disclaimer, "DRM");
        AssertContains(disclaimer, "paywalls");
        AssertContains(disclaimer, "authentication");
        AssertContains(disclaimer, "access controls");
    }

    [TestMethod]
    public void PrivacyCopy_CoversStorageCredentialsLogsTelemetryAndMetadata()
    {
        var privacyCopy = string.Join(
            " ",
            AppSubmissionInfo.PrivacySummary,
            AppSubmissionInfo.CredentialHandlingSummary,
            AppSubmissionInfo.SanitizedLogsSummary,
            AppSubmissionInfo.TelemetrySummary,
            AppSubmissionInfo.MetadataProviderSummary);

        AssertContains(privacyCopy, "on this device");
        AssertContains(privacyCopy, "source settings");
        AssertContains(privacyCopy, "credentials");
        AssertContains(privacyCopy, "DPAPI");
        AssertContains(privacyCopy, "sanitized");
        AssertContains(privacyCopy, "tokens");
        AssertContains(privacyCopy, "MAC-like");
        AssertContains(privacyCopy, "does not bundle");
        AssertContains(privacyCopy, "telemetry");
        AssertContains(privacyCopy, "TMDb");
        AssertContains(privacyCopy, "does not send source passwords");
    }

    [TestMethod]
    public void SupportCopy_CoversRequiredTroubleshootingTopics()
    {
        var supportCopy = string.Join(
            " ",
            AppSubmissionInfo.SupportSummary,
            AppSubmissionInfo.SupportAuthenticationFailure,
            AppSubmissionInfo.SupportNoChannels,
            AppSubmissionInfo.SupportNoEpg,
            AppSubmissionInfo.SupportWrongEpg,
            AppSubmissionInfo.SupportStreamDoesNotPlay,
            AppSubmissionInfo.SupportStoreInstall,
            AppSubmissionInfo.SupportResetAppData,
            AppSubmissionInfo.SupportExportDiagnostics);

        AssertContains(supportCopy, "Authentication failure");
        AssertContains(supportCopy, "No channels");
        AssertContains(supportCopy, "No EPG");
        AssertContains(supportCopy, "Wrong EPG");
        AssertContains(supportCopy, "Stream does not play");
        AssertContains(supportCopy, "Store or MSIX install");
        AssertContains(supportCopy, "Reset app data");
        AssertContains(supportCopy, "Export diagnostics");
        AssertContains(supportCopy, "sanitized diagnostics");
    }

    [TestMethod]
    public void RunFullTrustJustification_CoversDesktopPlaybackNeedAndAccessLimits()
    {
        var justification = AppSubmissionInfo.RunFullTrustJustification;

        AssertContains(justification, "WinUI 3 desktop application");
        AssertContains(justification, "Windows App SDK");
        AssertContains(justification, "native media playback");
        AssertContains(justification, "not used to bypass");
        AssertContains(justification, "DRM");
        AssertContains(justification, "paywalls");
        AssertContains(justification, "authentication");
        AssertContains(justification, "access controls");
    }

    private static void AssertContains(string haystack, string needle)
    {
        Assert.IsTrue(
            haystack.Contains(needle, StringComparison.OrdinalIgnoreCase),
            $"Expected '{needle}' in '{haystack}'.");
    }
}

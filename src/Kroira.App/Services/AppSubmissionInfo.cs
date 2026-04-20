#nullable enable
using System;

namespace Kroira.App.Services
{
    internal static class AppSubmissionInfo
    {
        internal const string AppName = "KROIRA";
        internal const string ProductDescription = "KROIRA is a local library and playback app for user-provided IPTV sources.";

        // Replace these values before final store submission as needed.
        internal const string PrivacyPolicyUrl = "https://sativagenetics.github.io/KroiraIPTV/privacy.html";
        internal const string SupportPageUrl = "https://sativagenetics.github.io/KroiraIPTV/support.html";
        internal const string SupportEmail = "";

        internal const string HelpStepOne = "1. Add a source.";
        internal const string HelpStepTwo = "2. Import or sync it.";
        internal const string HelpStepThree = "3. Browse Live TV, Movies, and Series.";
        internal const string HelpStepFour = "4. Guide availability depends on provider metadata and XMLTV advertising.";

        internal const string PrivacySummary = "Source details plus local favorites, playback history, watch state, and local preferences may be stored on this device.";
        internal const string SupportSummary = "When reporting issues, include the source type and recent import or guide diagnostics if available.";
        internal const string LegalDisclaimer = "KROIRA does not provide channels, movies, or series. It works with user-provided M3U and Xtream sources.";

        internal static bool HasPrivacyPolicyUrl => TryCreateUri(PrivacyPolicyUrl, out _);
        internal static bool HasSupportPageUrl => TryCreateUri(SupportPageUrl, out _);
        internal static bool HasSupportEmail => !string.IsNullOrWhiteSpace(SupportEmail);

        internal static string PrivacyPolicyDisplayText =>
            HasPrivacyPolicyUrl ? PrivacyPolicyUrl : "Add a privacy policy URL before store submission.";

        internal static string SupportPageDisplayText =>
            HasSupportPageUrl ? SupportPageUrl : "Add a support page URL before store submission.";

        internal static string SupportEmailDisplayText =>
            HasSupportEmail ? SupportEmail : "Add a support email before store submission.";

        internal static bool TryCreatePrivacyPolicyUri(out Uri? uri)
        {
            return TryCreateUri(PrivacyPolicyUrl, out uri);
        }

        internal static bool TryCreateSupportPageUri(out Uri? uri)
        {
            return TryCreateUri(SupportPageUrl, out uri);
        }

        internal static bool TryCreateSupportEmailUri(out Uri? uri)
        {
            if (!HasSupportEmail)
            {
                uri = null;
                return false;
            }

            return Uri.TryCreate($"mailto:{SupportEmail}", UriKind.Absolute, out uri);
        }

        private static bool TryCreateUri(string? value, out Uri? uri)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                uri = null;
                return false;
            }

            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri);
        }
    }
}

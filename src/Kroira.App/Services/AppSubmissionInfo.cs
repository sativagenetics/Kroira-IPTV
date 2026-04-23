#nullable enable
using System;

namespace Kroira.App.Services
{
    internal static class AppSubmissionInfo
    {
        internal const string AppName = "KROIRA";
        internal const string ProductDescription = "KROIRA is a Windows IPTV library and player for user-provided M3U, Xtream, and Stalker sources.";

        // Replace these values before final store submission as needed.
        internal const string PrivacyPolicyUrl = "https://sativagenetics.github.io/KroiraIPTV/privacy.html";
        internal const string SupportPageUrl = "https://sativagenetics.github.io/KroiraIPTV/support.html";
        internal const string SupportEmail = "batuhandemirbilek7@gmail.com";

        internal const string HelpStepOne = "1. Add an M3U playlist, Xtream provider, or Stalker portal.";
        internal const string HelpStepTwo = "2. Let KROIRA complete the first sync and health check.";
        internal const string HelpStepThree = "3. Browse Live TV, Movies, Series, Favorites, or Continue Watching.";
        internal const string HelpStepFour = "4. If guide coverage looks weak, review the source in Sources and adjust guide settings there.";

        internal const string PrivacySummary = "KROIRA stores source settings, favorites, playback progress, and local preferences on this device so your library stays consistent between launches.";
        internal const string SupportSummary = "When reporting an issue, include the source type, whether it affects Live TV or VOD, and the latest sync status from Sources.";
        internal const string LegalDisclaimer = "KROIRA is a player for user-provided IPTV sources. It does not include channels, movies, series, playlists, or subscriptions.";

        internal static bool HasPrivacyPolicyUrl => TryCreateUri(PrivacyPolicyUrl, out _);
        internal static bool HasSupportPageUrl => TryCreateUri(SupportPageUrl, out _);
        internal static bool HasSupportEmail => !string.IsNullOrWhiteSpace(SupportEmail);

        internal static string PrivacyPolicyDisplayText =>
            HasPrivacyPolicyUrl ? PrivacyPolicyUrl : "Privacy policy will be published before release.";

        internal static string SupportPageDisplayText =>
            HasSupportPageUrl ? SupportPageUrl : "Support page will be published before release.";

        internal static string SupportEmailDisplayText =>
            HasSupportEmail ? SupportEmail : "Support email will be published before release.";

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

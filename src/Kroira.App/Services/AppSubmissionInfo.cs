#nullable enable
using System;

namespace Kroira.App.Services
{
    internal static class AppSubmissionInfo
    {
        internal const string AppName = "KROIRA IPTV";
        internal const string ReleaseVersion = "2.0.0";
        internal const string ShortDescription = "Bring your own IPTV sources to a local-first Windows media player and source manager.";
        internal const string LongDescription =
            "KROIRA IPTV is a Windows media player and source manager for user-provided M3U playlists, Xtream providers, and Stalker portals. " +
            "It helps organize live channels, VOD libraries, guide data, favorites, continue-watching state, and source diagnostics on this device. " +
            "KROIRA does not provide channels, streams, playlists, subscriptions, accounts, credentials, or media content.";
        internal const string ProductDescription = LongDescription;

        internal const string PrivacyPolicyUrl = "https://sativagenetics.github.io/KroiraIPTV/privacy.html";
        internal const string SupportPageUrl = "https://sativagenetics.github.io/KroiraIPTV/support.html";
        internal const string SupportEmail = "batuhandemirbilek7@gmail.com";

        internal const string HelpStepOne = "1. Add an M3U playlist, Xtream provider, or Stalker portal.";
        internal const string HelpStepTwo = "2. Let KROIRA complete the first sync and health check.";
        internal const string HelpStepThree = "3. Browse Live TV, Movies, Series, Favorites, or Continue Watching.";
        internal const string HelpStepFour = "4. If guide coverage looks weak, review the source in Sources and adjust guide settings there.";

        internal const string PrivacySummary =
            "KROIRA stores source settings, guide state, favorites, playback progress, diagnostics, and local preferences on this device. " +
            "The app does not include bundled telemetry or advertising analytics.";
        internal const string CredentialHandlingSummary =
            "Source credentials are used only to connect to sources the user configures. Protected credential copies use Windows DPAPI CurrentUser scope when available, with plaintext compatibility retained for existing import and parser flows.";
        internal const string SanitizedLogsSummary =
            "Sanitized diagnostics and exported source reports are designed to mask playlist URLs, usernames, passwords, tokens, keys, MAC-like values, and loose secrets before display or export.";
        internal const string TelemetrySummary =
            "KROIRA does not bundle app telemetry, advertising analytics, or a vendor-operated media URL collection service in this release.";
        internal const string MetadataProviderSummary =
            "When metadata enrichment is used, KROIRA may contact metadata or artwork providers such as TMDb, plus image URLs supplied by user-configured sources. KROIRA does not send source passwords or provider tokens to TMDb.";
        internal const string SupportSummary =
            "When reporting an issue, include the source type, affected area, latest sync or health status, and a sanitized diagnostics export when available.";
        internal const string SupportAuthenticationFailure =
            "Authentication failure: verify the provider URL, username, password, account status, and any portal/MAC details with the source provider. KROIRA cannot recover or issue provider credentials.";
        internal const string SupportNoChannels =
            "No channels: resync the source, confirm the playlist or provider account has active live/VOD/series entries, and review source diagnostics for invalid, duplicate, or filtered entries.";
        internal const string SupportNoEpg =
            "No EPG: confirm the source advertises an XMLTV URL or configure a manual EPG URL, then run an EPG sync from Sources or Guide.";
        internal const string SupportWrongEpg =
            "Wrong EPG: open manual EPG matching from Guide or source diagnostics, search the XMLTV channel, set or clear an override, and resync guide data.";
        internal const string SupportStreamDoesNotPlay =
            "Stream does not play: verify the stream URL works for the account, check whether the source requires authentication, proxy, or companion routing, and run a limited source probe from diagnostics.";
        internal const string SupportStoreInstall =
            "Store or MSIX install: install from Microsoft Store or a trusted signed package, then restart Windows if the Windows App SDK runtime was updated.";
        internal const string SupportResetAppData =
            "Reset app data: use Windows Settings > Apps > Installed apps > KROIRA IPTV > Advanced options > Reset. This removes local sources, credentials, guide data, favorites, and preferences.";
        internal const string SupportExportDiagnostics =
            "Export diagnostics: use the source diagnostics export action. Exports are sanitized, but review files before sharing because provider names and non-secret configuration may remain.";
        internal const string LegalDisclaimer =
            "KROIRA is a media player and source manager. KROIRA does not provide channels, streams, playlists, subscriptions, or media content. " +
            "Users are responsible for adding only authorized sources and for complying with applicable provider terms and laws. " +
            "KROIRA does not bypass DRM, paywalls, authentication, or access controls.";
        internal const string RunFullTrustJustification =
            "KROIRA is a packaged WinUI 3 desktop application that uses the Windows App SDK full-trust desktop entry point and local native playback components. " +
            "The runFullTrust capability is required for the packaged desktop app launch model and native media playback integration; it is not used to bypass DRM, paywalls, authentication, or access controls.";

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

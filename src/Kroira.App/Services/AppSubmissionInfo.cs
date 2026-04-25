#nullable enable
using System;

namespace Kroira.App.Services
{
    internal static class AppSubmissionInfo
    {
        internal const string AppName = "KROIRA IPTV";
        internal const string ReleaseVersion = "2.0.0";
        internal static string ShortDescription => LocalizedStrings.Get("Submission_ShortDescription");
        internal static string LongDescription => LocalizedStrings.Get("Submission_LongDescription");
        internal static string ProductDescription => LongDescription;

        internal const string PrivacyPolicyUrl = "https://sativagenetics.github.io/KroiraIPTV/privacy.html";
        internal const string SupportPageUrl = "https://sativagenetics.github.io/KroiraIPTV/support.html";
        internal const string SupportEmail = "batuhandemirbilek7@gmail.com";

        internal static string HelpStepOne => LocalizedStrings.Get("Submission_Help_Step1");
        internal static string HelpStepTwo => LocalizedStrings.Get("Submission_Help_Step2");
        internal static string HelpStepThree => LocalizedStrings.Get("Submission_Help_Step3");
        internal static string HelpStepFour => LocalizedStrings.Get("Submission_Help_Step4");

        internal static string PrivacySummary => LocalizedStrings.Get("Submission_PrivacySummary");
        internal static string CredentialHandlingSummary => LocalizedStrings.Get("Submission_CredentialHandlingSummary");
        internal static string SanitizedLogsSummary => LocalizedStrings.Get("Submission_SanitizedLogsSummary");
        internal static string TelemetrySummary => LocalizedStrings.Get("Submission_TelemetrySummary");
        internal static string MetadataProviderSummary => LocalizedStrings.Get("Submission_MetadataProviderSummary");
        internal static string SupportSummary => LocalizedStrings.Get("Submission_SupportSummary");
        internal static string SupportAuthenticationFailure => LocalizedStrings.Get("Submission_Support_AuthenticationFailure");
        internal static string SupportNoChannels => LocalizedStrings.Get("Submission_Support_NoChannels");
        internal static string SupportNoEpg => LocalizedStrings.Get("Submission_Support_NoEpg");
        internal static string SupportWrongEpg => LocalizedStrings.Get("Submission_Support_WrongEpg");
        internal static string SupportStreamDoesNotPlay => LocalizedStrings.Get("Submission_Support_StreamDoesNotPlay");
        internal static string SupportStoreInstall => LocalizedStrings.Get("Submission_Support_StoreInstall");
        internal static string SupportResetAppData => LocalizedStrings.Get("Submission_Support_ResetAppData");
        internal static string SupportExportDiagnostics => LocalizedStrings.Get("Submission_Support_ExportDiagnostics");
        internal static string LegalDisclaimer => LocalizedStrings.Get("Submission_LegalDisclaimer");
        internal static string RunFullTrustJustification => LocalizedStrings.Get("Submission_RunFullTrustJustification");

        internal static bool HasPrivacyPolicyUrl => TryCreateUri(PrivacyPolicyUrl, out _);
        internal static bool HasSupportPageUrl => TryCreateUri(SupportPageUrl, out _);
        internal static bool HasSupportEmail => !string.IsNullOrWhiteSpace(SupportEmail);

        internal static string PrivacyPolicyDisplayText =>
            HasPrivacyPolicyUrl ? PrivacyPolicyUrl : LocalizedStrings.Get("Submission_PrivacyPolicyUnavailable");

        internal static string SupportPageDisplayText =>
            HasSupportPageUrl ? SupportPageUrl : LocalizedStrings.Get("Submission_SupportPageUnavailable");

        internal static string SupportEmailDisplayText =>
            HasSupportEmail ? SupportEmail : LocalizedStrings.Get("Submission_SupportEmailUnavailable");

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

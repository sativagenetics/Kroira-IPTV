#nullable enable
using System;

namespace Kroira.App.Services
{
    internal static class AppSubmissionInfo
    {
        internal const string AppName = "KROIRA IPTV";
        internal const string ReleaseVersion = "2.0.0";
        internal static string ShortDescription => LocalizedStrings.Get("Submission.ShortDescription");
        internal static string LongDescription => LocalizedStrings.Get("Submission.LongDescription");
        internal static string ProductDescription => LongDescription;

        internal const string PrivacyPolicyUrl = "https://sativagenetics.github.io/KroiraIPTV/privacy.html";
        internal const string SupportPageUrl = "https://sativagenetics.github.io/KroiraIPTV/support.html";
        internal const string SupportEmail = "batuhandemirbilek7@gmail.com";

        internal static string HelpStepOne => LocalizedStrings.Get("Submission.Help.Step1");
        internal static string HelpStepTwo => LocalizedStrings.Get("Submission.Help.Step2");
        internal static string HelpStepThree => LocalizedStrings.Get("Submission.Help.Step3");
        internal static string HelpStepFour => LocalizedStrings.Get("Submission.Help.Step4");

        internal static string PrivacySummary => LocalizedStrings.Get("Submission.PrivacySummary");
        internal static string CredentialHandlingSummary => LocalizedStrings.Get("Submission.CredentialHandlingSummary");
        internal static string SanitizedLogsSummary => LocalizedStrings.Get("Submission.SanitizedLogsSummary");
        internal static string TelemetrySummary => LocalizedStrings.Get("Submission.TelemetrySummary");
        internal static string MetadataProviderSummary => LocalizedStrings.Get("Submission.MetadataProviderSummary");
        internal static string SupportSummary => LocalizedStrings.Get("Submission.SupportSummary");
        internal static string SupportAuthenticationFailure => LocalizedStrings.Get("Submission.Support.AuthenticationFailure");
        internal static string SupportNoChannels => LocalizedStrings.Get("Submission.Support.NoChannels");
        internal static string SupportNoEpg => LocalizedStrings.Get("Submission.Support.NoEpg");
        internal static string SupportWrongEpg => LocalizedStrings.Get("Submission.Support.WrongEpg");
        internal static string SupportStreamDoesNotPlay => LocalizedStrings.Get("Submission.Support.StreamDoesNotPlay");
        internal static string SupportStoreInstall => LocalizedStrings.Get("Submission.Support.StoreInstall");
        internal static string SupportResetAppData => LocalizedStrings.Get("Submission.Support.ResetAppData");
        internal static string SupportExportDiagnostics => LocalizedStrings.Get("Submission.Support.ExportDiagnostics");
        internal static string LegalDisclaimer => LocalizedStrings.Get("Submission.LegalDisclaimer");
        internal static string RunFullTrustJustification => LocalizedStrings.Get("Submission.RunFullTrustJustification");

        internal static bool HasPrivacyPolicyUrl => TryCreateUri(PrivacyPolicyUrl, out _);
        internal static bool HasSupportPageUrl => TryCreateUri(SupportPageUrl, out _);
        internal static bool HasSupportEmail => !string.IsNullOrWhiteSpace(SupportEmail);

        internal static string PrivacyPolicyDisplayText =>
            HasPrivacyPolicyUrl ? PrivacyPolicyUrl : LocalizedStrings.Get("Submission.PrivacyPolicyUnavailable");

        internal static string SupportPageDisplayText =>
            HasSupportPageUrl ? SupportPageUrl : LocalizedStrings.Get("Submission.SupportPageUnavailable");

        internal static string SupportEmailDisplayText =>
            HasSupportEmail ? SupportEmail : LocalizedStrings.Get("Submission.SupportEmailUnavailable");

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

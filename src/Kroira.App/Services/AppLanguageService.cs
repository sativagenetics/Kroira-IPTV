#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Windows.Storage;
using MicrosoftApplicationLanguages = Microsoft.Windows.Globalization.ApplicationLanguages;
using WindowsApplicationLanguages = Windows.Globalization.ApplicationLanguages;

namespace Kroira.App.Services
{
    public sealed class AppLanguageOption
    {
        public AppLanguageOption(string code, string displayNameResourceKey)
        {
            Code = code;
            DisplayNameResourceKey = displayNameResourceKey;
        }

        public string Code { get; }
        public string DisplayNameResourceKey { get; }
    }

    public static class AppLanguageService
    {
        public const string LanguageSettingKey = "App.Language";
        public const string SystemDefaultLanguageCode = "system";
        public const string DefaultLanguageCode = "en-US";
        public const string ArabicLanguageCode = "ar-SA";

        public static readonly IReadOnlyList<AppLanguageOption> SupportedLanguages = new[]
        {
            new AppLanguageOption(SystemDefaultLanguageCode, "Language_SystemDefault"),
            new AppLanguageOption(DefaultLanguageCode, "Language_English"),
            new AppLanguageOption("tr-TR", "Language_Turkish"),
            new AppLanguageOption("zh-Hans", "Language_ChineseSimplified"),
            new AppLanguageOption("es-ES", "Language_Spanish"),
            new AppLanguageOption(ArabicLanguageCode, "Language_Arabic"),
            new AppLanguageOption("fr-FR", "Language_French"),
            new AppLanguageOption("de-DE", "Language_German"),
            new AppLanguageOption("pt-BR", "Language_PortugueseBrazil"),
            new AppLanguageOption("hi-IN", "Language_Hindi"),
            new AppLanguageOption("ja-JP", "Language_Japanese"),
            new AppLanguageOption("ko-KR", "Language_Korean")
        };

        public static IReadOnlyList<string> SupportedLanguageCodes =>
            SupportedLanguages.Select(language => language.Code).ToArray();

        public static event EventHandler<AppLanguageChangedEventArgs>? LanguageChanged;

        public static void ApplySavedLanguageOverride()
        {
            if (TryGetPersistedLanguageCode(out var languageCode))
            {
                ApplyLanguageOverride(languageCode);
            }
        }

        public static void ApplyLanguageOverride(string? languageCode)
        {
            var normalized = NormalizeLanguageCode(languageCode);
            if (string.Equals(normalized, SystemDefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                ClearPrimaryLanguageOverride();
                ApplyCurrentCulture(SystemDefaultLanguageCode);
                LocalizedStrings.Reset();
                return;
            }

            SetPrimaryLanguageOverride(normalized);
            ApplyCurrentCulture(normalized);
            LocalizedStrings.Reset();
        }

        public static async Task<string> GetLanguageAsync(AppDbContext db, int profileId = 0)
        {
            if (TryGetPersistedLanguageCode(out var persisted))
            {
                return persisted;
            }

            var scopedKey = BuildScopedLanguageKey(profileId);
            var value = await db.AppSettings
                .Where(setting => setting.Key == scopedKey)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            if (value == null && profileId > 0)
            {
                value = await db.AppSettings
                    .Where(setting => setting.Key == LanguageSettingKey)
                    .Select(setting => setting.Value)
                    .FirstOrDefaultAsync();
            }

            return NormalizeLanguageCode(value);
        }

        public static async Task SetLanguageAsync(AppDbContext db, string languageCode, int profileId = 0)
        {
            var previous = GetPersistedLanguageCode();
            var normalized = NormalizeLanguageCode(languageCode);
            PersistLanguageCode(normalized);
            ApplyLanguageOverride(normalized);

            var scopedKey = BuildScopedLanguageKey(profileId);

            var setting = await db.AppSettings.FirstOrDefaultAsync(item => item.Key == scopedKey);
            if (setting == null)
            {
                setting = new AppSetting
                {
                    Key = scopedKey,
                    Value = normalized
                };
                db.AppSettings.Add(setting);
            }
            else
            {
                setting.Value = normalized;
            }

            await db.SaveChangesAsync();

            if (!string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase))
            {
                LanguageChanged?.Invoke(
                    null,
                    new AppLanguageChangedEventArgs(previous, normalized, LocalizedStrings.Version));
            }
        }

        public static string NormalizeLanguageCode(string? languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return SystemDefaultLanguageCode;
            }

            var trimmed = languageCode.Trim();
            return SupportedLanguages.Any(language => string.Equals(language.Code, trimmed, StringComparison.OrdinalIgnoreCase))
                ? SupportedLanguages.First(language => string.Equals(language.Code, trimmed, StringComparison.OrdinalIgnoreCase)).Code
                : SystemDefaultLanguageCode;
        }

        public static bool IsCurrentLanguageRightToLeft()
        {
            var explicitOverride = GetCurrentPrimaryLanguageOverride();
            var candidate = !string.IsNullOrWhiteSpace(explicitOverride)
                ? explicitOverride
                : Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault();

            return !string.IsNullOrWhiteSpace(candidate) &&
                candidate.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        }

        public static FlowDirection GetCurrentFlowDirection()
        {
            return IsCurrentLanguageRightToLeft()
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }

        public static string GetPersistedLanguageCode()
        {
            return TryGetPersistedLanguageCode(out var languageCode)
                ? languageCode
                : SystemDefaultLanguageCode;
        }

        private static bool TryGetPersistedLanguageCode(out string languageCode)
        {
            try
            {
                var rawValue = ApplicationData.Current.LocalSettings.Values[LanguageSettingKey] as string;
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    languageCode = SystemDefaultLanguageCode;
                    return false;
                }

                languageCode = NormalizeLanguageCode(rawValue);
                return true;
            }
            catch
            {
                languageCode = SystemDefaultLanguageCode;
                return false;
            }
        }

        private static void PersistLanguageCode(string languageCode)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[LanguageSettingKey] = NormalizeLanguageCode(languageCode);
            }
            catch
            {
                // LocalSettings can be unavailable in some design-time or headless test hosts.
            }
        }

        private static string BuildScopedLanguageKey(int profileId)
        {
            return profileId > 0
                ? $"{LanguageSettingKey}.Profile.{profileId}"
                : LanguageSettingKey;
        }

        private static void SetPrimaryLanguageOverride(string languageCode)
        {
            try
            {
                MicrosoftApplicationLanguages.PrimaryLanguageOverride = languageCode;
                return;
            }
            catch
            {
                // Some Windows App SDK hosts can be stricter than the packaged Windows.Globalization API.
            }

            try
            {
                WindowsApplicationLanguages.PrimaryLanguageOverride = languageCode;
            }
            catch
            {
                // Leave Windows' language selection in place rather than failing app startup.
            }
        }

        private static void ClearPrimaryLanguageOverride()
        {
            if (string.IsNullOrWhiteSpace(GetCurrentPrimaryLanguageOverride()))
            {
                return;
            }

            if (TryClearPrimaryLanguageOverrideWithWindowsApi())
            {
                return;
            }

            try
            {
                MicrosoftApplicationLanguages.PrimaryLanguageOverride = string.Empty;
            }
            catch
            {
                // Clearing is best-effort on hosts that reject an empty override value.
            }
        }

        private static void ApplyCurrentCulture(string languageCode)
        {
            var candidate = string.Equals(languageCode, SystemDefaultLanguageCode, StringComparison.OrdinalIgnoreCase)
                ? ResolveSystemLanguageCode()
                : languageCode;

            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = DefaultLanguageCode;
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(candidate);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
            catch
            {
                // Keep the host culture if Windows reports an unsupported tag.
            }
        }

        private static string ResolveSystemLanguageCode()
        {
            var userLanguages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            foreach (var userLanguage in userLanguages)
            {
                var match = SupportedLanguages
                    .Where(language => language.Code != SystemDefaultLanguageCode)
                    .FirstOrDefault(language =>
                        string.Equals(language.Code, userLanguage, StringComparison.OrdinalIgnoreCase) ||
                        userLanguage.StartsWith(language.Code, StringComparison.OrdinalIgnoreCase) ||
                        language.Code.StartsWith(userLanguage, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match.Code;
                }
            }

            return DefaultLanguageCode;
        }

        private static bool TryClearPrimaryLanguageOverrideWithWindowsApi()
        {
            try
            {
                WindowsApplicationLanguages.PrimaryLanguageOverride = string.Empty;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetCurrentPrimaryLanguageOverride()
        {
            try
            {
                var languageCode = MicrosoftApplicationLanguages.PrimaryLanguageOverride;
                if (!string.IsNullOrWhiteSpace(languageCode))
                {
                    return languageCode;
                }
            }
            catch
            {
                // Fall back to the packaged Windows.Globalization API below.
            }

            try
            {
                return WindowsApplicationLanguages.PrimaryLanguageOverride ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public sealed class AppLanguageChangedEventArgs : EventArgs
    {
        public AppLanguageChangedEventArgs(string previousLanguageCode, string currentLanguageCode, int version)
        {
            PreviousLanguageCode = previousLanguageCode;
            CurrentLanguageCode = currentLanguageCode;
            Version = version;
        }

        public string PreviousLanguageCode { get; }
        public string CurrentLanguageCode { get; }
        public int Version { get; }
    }
}

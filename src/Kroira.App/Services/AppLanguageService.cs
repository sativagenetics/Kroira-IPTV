#nullable enable
using System;
using System.Collections.Generic;
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
            new AppLanguageOption(SystemDefaultLanguageCode, "Language.SystemDefault"),
            new AppLanguageOption(DefaultLanguageCode, "Language.English"),
            new AppLanguageOption("tr-TR", "Language.Turkish"),
            new AppLanguageOption("zh-Hans", "Language.ChineseSimplified"),
            new AppLanguageOption("es-ES", "Language.Spanish"),
            new AppLanguageOption(ArabicLanguageCode, "Language.Arabic"),
            new AppLanguageOption("fr-FR", "Language.French"),
            new AppLanguageOption("de-DE", "Language.German"),
            new AppLanguageOption("pt-BR", "Language.PortugueseBrazil"),
            new AppLanguageOption("hi-IN", "Language.Hindi"),
            new AppLanguageOption("ja-JP", "Language.Japanese"),
            new AppLanguageOption("ko-KR", "Language.Korean")
        };

        public static IReadOnlyList<string> SupportedLanguageCodes =>
            SupportedLanguages.Select(language => language.Code).ToArray();

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
                return;
            }

            SetPrimaryLanguageOverride(normalized);
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
}

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public static class AppLanguageService
    {
        public const string LanguageSettingKey = "App.Language";
        public const string DefaultLanguageCode = "tr-TR";

        public static readonly string[] SupportedLanguageCodes = { DefaultLanguageCode };

        public static async Task<string> GetLanguageAsync(AppDbContext db, int profileId = 0)
        {
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
            return SupportedLanguageCodes.Contains(languageCode)
                ? languageCode!
                : DefaultLanguageCode;
        }

        private static string BuildScopedLanguageKey(int profileId)
        {
            return profileId > 0
                ? $"{LanguageSettingKey}.Profile.{profileId}"
                : LanguageSettingKey;
        }
    }
}

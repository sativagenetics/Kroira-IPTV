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

        public static async Task<string> GetLanguageAsync(AppDbContext db)
        {
            var value = await db.AppSettings
                .Where(setting => setting.Key == LanguageSettingKey)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(value) ? DefaultLanguageCode : value;
        }

        public static async Task SetLanguageAsync(AppDbContext db, string languageCode)
        {
            var normalized = string.IsNullOrWhiteSpace(languageCode)
                ? DefaultLanguageCode
                : languageCode;

            var setting = await db.AppSettings.FirstOrDefaultAsync(item => item.Key == LanguageSettingKey);
            if (setting == null)
            {
                setting = new AppSetting
                {
                    Key = LanguageSettingKey,
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
    }
}

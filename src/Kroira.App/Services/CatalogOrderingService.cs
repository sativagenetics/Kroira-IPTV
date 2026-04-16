using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kroira.App.Services
{
    public static class CatalogOrderingService
    {
        public static IOrderedEnumerable<T> OrderCatalog<T>(
            IEnumerable<T> items,
            string languageCode,
            Func<T, string> categorySelector,
            Func<T, string> titleSelector)
        {
            return items
                .OrderBy(item => GetLanguageRank(languageCode, categorySelector(item), titleSelector(item)))
                .ThenBy(titleSelector, StringComparer.CurrentCultureIgnoreCase);
        }

        public static IOrderedEnumerable<string> OrderCategories(IEnumerable<string> categories, string languageCode)
        {
            return categories
                .OrderBy(category => GetLanguageRank(languageCode, category, string.Empty))
                .ThenBy(category => category, StringComparer.CurrentCultureIgnoreCase);
        }

        private static int GetLanguageRank(string languageCode, string category, string title)
        {
            var selectedLanguage = GetLanguageFamily(languageCode);

            if (MatchesLanguage(selectedLanguage, category, title))
            {
                return 0;
            }

            if (selectedLanguage != "tr" && MatchesLanguage("tr", category, title))
            {
                return 1;
            }

            return 2;
        }

        private static string GetLanguageFamily(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return "tr";
            }

            try
            {
                return new CultureInfo(languageCode).TwoLetterISOLanguageName.ToLowerInvariant();
            }
            catch
            {
                return languageCode.Split('-')[0].ToLowerInvariant();
            }
        }

        private static bool MatchesLanguage(string languageFamily, string category, string title)
        {
            var categoryText = Normalize(category);
            var titleText = Normalize(title);
            var combined = $"{categoryText} {titleText}";

            return languageFamily switch
            {
                "tr" => HasAnyToken(categoryText, "TR", "TURK", "TURKEY", "TURKISH", "TURKIYE") ||
                        combined.Contains("TURK", StringComparison.Ordinal) ||
                        combined.Contains("TURKIYE", StringComparison.Ordinal),
                "de" => HasAnyToken(categoryText, "DE", "GER", "GERMANY", "GERMAN", "DEUTSCH", "DEUTSCHLAND") ||
                        combined.Contains("GERMAN", StringComparison.Ordinal) ||
                        combined.Contains("DEUTSCH", StringComparison.Ordinal),
                "en" => HasAnyToken(categoryText, "EN", "ENG", "UK", "US", "USA", "ENGLISH") ||
                        combined.Contains("ENGLISH", StringComparison.Ordinal),
                "ar" => HasAnyToken(categoryText, "AR", "ARABIC", "ARAB") ||
                        combined.Contains("ARABIC", StringComparison.Ordinal),
                _ => false
            };
        }

        private static bool HasAnyToken(string value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(
                new[] { ' ', '/', '\\', '|', '-', '_', ':', ';', '.', ',', '[', ']', '(', ')' },
                StringSplitOptions.RemoveEmptyEntries);

            return parts.Any(part => tokens.Contains(part, StringComparer.OrdinalIgnoreCase));
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .ToUpperInvariant()
                .Replace('\u00dc', 'U')
                .Replace('\u011e', 'G')
                .Replace('\u0130', 'I')
                .Replace('\u015e', 'S')
                .Replace('\u00d6', 'O')
                .Replace('\u00c7', 'C');
        }
    }
}

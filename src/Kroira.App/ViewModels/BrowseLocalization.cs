#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kroira.App.Models;
using Kroira.App.Services;

namespace Kroira.App.ViewModels
{
    internal readonly record struct BrowseSortOptionSpec(string Key, string LabelResourceKey);

    internal static class BrowseLocalization
    {
        public static readonly IReadOnlyList<BrowseSortOptionSpec> LiveSortOptions =
        [
            new("priority_first", "Browse_Sort_PriorityFirst"),
            new("name_desc", "Browse_Sort_NameDesc"),
            new("name_asc", "Browse_Sort_NameAsc"),
            new("favorites_first", "Browse_Sort_FavoritesFirst"),
            new("guide_first", "Browse_Sort_GuideFirst")
        ];

        public static readonly IReadOnlyList<BrowseSortOptionSpec> MovieSortOptions =
        [
            new("recommended", "Browse_Sort_Recommended"),
            new("title_asc", "Browse_Sort_TitleAsc"),
            new("rating_desc", "Browse_Sort_HighestRated"),
            new("popularity_desc", "Browse_Sort_MostPopular"),
            new("year_desc", "Browse_Sort_NewestRelease"),
            new("favorites_first", "Browse_Sort_FavoritesFirst")
        ];

        public static readonly IReadOnlyList<BrowseSortOptionSpec> SeriesSortOptions =
        [
            new("recommended", "Browse_Sort_Recommended"),
            new("title_asc", "Browse_Sort_TitleAsc"),
            new("rating_desc", "Browse_Sort_HighestRated"),
            new("popularity_desc", "Browse_Sort_MostPopular"),
            new("year_desc", "Browse_Sort_NewestFirst"),
            new("favorites_first", "Browse_Sort_FavoritesFirst")
        ];

        public static IEnumerable<BrowseSortOptionViewModel> CreateSortOptions(IEnumerable<BrowseSortOptionSpec> specs)
        {
            return specs.Select(spec => new BrowseSortOptionViewModel(spec.Key, LocalizedStrings.Get(spec.LabelResourceKey)));
        }

        public static string AllProviders => LocalizedStrings.Get("Browse_AllProviders");
        public static string OriginalProviderGroups => LocalizedStrings.Get("Browse_OriginalProviderGroups");
        public static string VodLibrary => LocalizedStrings.Get("Browse_VodLibrary");

        public static string SourceFallback(int id)
        {
            return LocalizedStrings.Format("Browse_SourceFallback", id);
        }

        public static string ChannelCount(int count)
        {
            return count == 1
                ? LocalizedStrings.Get("Browse_ChannelCount_One")
                : LocalizedStrings.Format("Browse_ChannelCount_Many", count);
        }

        public static string VariantCount(int count)
        {
            return count == 1
                ? LocalizedStrings.Get("Browse_VariantCount_One")
                : LocalizedStrings.Format("Browse_VariantCount_Many", count);
        }

        public static string MoreCount(int count)
        {
            return LocalizedStrings.Format("Browse_MoreCount", count);
        }

        public static string SmartCategoryName(SmartCategoryDefinition definition)
        {
            return LocalizedStrings.GetOrDefault(
                $"SmartCategory_{SanitizeResourceSuffix(definition.Id)}_Name",
                definition.DisplayName);
        }

        public static string SmartCategoryDescription(SmartCategoryDefinition definition)
        {
            return LocalizedStrings.GetOrDefault(
                $"SmartCategory_{SanitizeResourceSuffix(definition.Id)}_Description",
                definition.MatchRule);
        }

        public static string SmartCategorySection(SmartCategoryDefinition definition)
        {
            return LocalizedStrings.GetOrDefault(
                $"SmartCategorySection_{SanitizeResourceSuffix(definition.SectionName)}",
                definition.SectionName);
        }

        public static string SmartCategorySection(string sectionName)
        {
            return LocalizedStrings.GetOrDefault(
                $"SmartCategorySection_{SanitizeResourceSuffix(sectionName)}",
                sectionName);
        }

        private static string SanitizeResourceSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "General";
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }

            var suffix = builder.ToString();
            while (suffix.Contains("__", StringComparison.Ordinal))
            {
                suffix = suffix.Replace("__", "_", StringComparison.Ordinal);
            }

            return suffix.Trim('_');
        }
    }
}

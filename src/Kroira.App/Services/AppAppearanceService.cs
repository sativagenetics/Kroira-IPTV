#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kroira.App.Services
{
    public sealed class AppAppearanceSettings
    {
        public string ThemePresetKey { get; set; } = "cinema";
        public string AccentPresetKey { get; set; } = "gold";
    }

    public sealed class AppAppearanceOption
    {
        public AppAppearanceOption(string key, string displayName, string description)
        {
            Key = key;
            DisplayName = displayName;
            Description = description;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
    }

    public interface IAppAppearanceService
    {
        IReadOnlyList<AppAppearanceOption> ThemeOptions { get; }
        IReadOnlyList<AppAppearanceOption> AccentOptions { get; }
        Task InitializeAsync();
        Task<AppAppearanceSettings> LoadAsync(AppDbContext db);
        Task SaveAsync(AppDbContext db, AppAppearanceSettings settings);
        void Apply(AppAppearanceSettings settings);
    }

    public sealed class AppAppearanceService : IAppAppearanceService
    {
        private const string ThemeSettingKey = "Appearance.ThemePreset";
        private const string AccentSettingKey = "Appearance.AccentPreset";
        private readonly IServiceProvider _serviceProvider;

        private sealed record AppAppearanceOptionSpec(
            string Key,
            string DisplayNameResourceKey,
            string DescriptionResourceKey);

        private static readonly IReadOnlyList<AppAppearanceOptionSpec> ThemeOptionSpecs = new[]
        {
            new AppAppearanceOptionSpec("cinema", "Appearance_Theme_Cinema_Name", "Appearance_Theme_Cinema_Description"),
            new AppAppearanceOptionSpec("broadcast", "Appearance_Theme_Broadcast_Name", "Appearance_Theme_Broadcast_Description"),
            new AppAppearanceOptionSpec("archive", "Appearance_Theme_Archive_Name", "Appearance_Theme_Archive_Description")
        };

        private static readonly IReadOnlyList<AppAppearanceOptionSpec> AccentOptionSpecs = new[]
        {
            new AppAppearanceOptionSpec("gold", "Appearance_Accent_Gold_Name", "Appearance_Accent_Gold_Description"),
            new AppAppearanceOptionSpec("signal", "Appearance_Accent_Signal_Name", "Appearance_Accent_Signal_Description"),
            new AppAppearanceOptionSpec("ember", "Appearance_Accent_Ember_Name", "Appearance_Accent_Ember_Description")
        };

        public AppAppearanceService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IReadOnlyList<AppAppearanceOption> ThemeOptions => ThemeOptionSpecs.Select(CreateOption).ToList();
        public IReadOnlyList<AppAppearanceOption> AccentOptions => AccentOptionSpecs.Select(CreateOption).ToList();

        public async Task InitializeAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await LoadAsync(db);
            Apply(settings);
        }

        public async Task<AppAppearanceSettings> LoadAsync(AppDbContext db)
        {
            var values = await db.AppSettings
                .Where(setting => setting.Key == ThemeSettingKey || setting.Key == AccentSettingKey)
                .ToDictionaryAsync(setting => setting.Key, setting => setting.Value);

            var settings = new AppAppearanceSettings
            {
                ThemePresetKey = values.TryGetValue(ThemeSettingKey, out var theme) ? NormalizeThemeKey(theme) : "cinema",
                AccentPresetKey = values.TryGetValue(AccentSettingKey, out var accent) ? NormalizeAccentKey(accent) : "gold"
            };

            return settings;
        }

        public async Task SaveAsync(AppDbContext db, AppAppearanceSettings settings)
        {
            await SaveSettingAsync(db, ThemeSettingKey, NormalizeThemeKey(settings.ThemePresetKey));
            await SaveSettingAsync(db, AccentSettingKey, NormalizeAccentKey(settings.AccentPresetKey));
            Apply(settings);
        }

        public void Apply(AppAppearanceSettings settings)
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
            {
                return;
            }

            ApplyThemePreset(resources, NormalizeThemeKey(settings.ThemePresetKey));
            ApplyAccentPreset(resources, NormalizeAccentKey(settings.AccentPresetKey));
        }

        private static async Task SaveSettingAsync(AppDbContext db, string key, string value)
        {
            var setting = await db.AppSettings.FirstOrDefaultAsync(existing => existing.Key == key);
            if (setting == null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value
                });
            }
            else
            {
                setting.Value = value;
            }

            await db.SaveChangesAsync();
        }

        private static string NormalizeThemeKey(string? value)
        {
            return ThemeOptionSpecs.Any(option => string.Equals(option.Key, value, StringComparison.OrdinalIgnoreCase))
                ? value!.Trim().ToLowerInvariant()
                : "cinema";
        }

        private static string NormalizeAccentKey(string? value)
        {
            return AccentOptionSpecs.Any(option => string.Equals(option.Key, value, StringComparison.OrdinalIgnoreCase))
                ? value!.Trim().ToLowerInvariant()
                : "gold";
        }

        private static AppAppearanceOption CreateOption(AppAppearanceOptionSpec spec)
        {
            return new AppAppearanceOption(
                spec.Key,
                LocalizedStrings.Get(spec.DisplayNameResourceKey),
                LocalizedStrings.Get(spec.DescriptionResourceKey));
        }

        private static void ApplyThemePreset(ResourceDictionary resources, string key)
        {
            switch (key)
            {
                case "broadcast":
                    SetGradientStops(resources, "KroiraPageAtmosphereBrush", Parse("#111A24"), Parse("#090D13"), Parse("#040608"));
                    SetGradientStops(resources, "KroiraShellGradientBrush", Parse("#0C1721"), Parse("#090D13"), Parse("#030405"));
                    SetGradientStops(resources, "KroiraMediaGradientBrush", Parse("#1E3140"), Parse("#151E28"), Parse("#090A0D"));
                    SetSolidBrush(resources, "KroiraSurfaceBrush", Parse("#141C26"));
                    SetSolidBrush(resources, "KroiraSurfaceElevatedBrush", Parse("#1A2430"));
                    break;
                case "archive":
                    SetGradientStops(resources, "KroiraPageAtmosphereBrush", Parse("#19170F"), Parse("#0B0C0D"), Parse("#050506"));
                    SetGradientStops(resources, "KroiraShellGradientBrush", Parse("#1C150D"), Parse("#0B0C0D"), Parse("#040405"));
                    SetGradientStops(resources, "KroiraMediaGradientBrush", Parse("#372B1D"), Parse("#191D1F"), Parse("#090A0D"));
                    SetSolidBrush(resources, "KroiraSurfaceBrush", Parse("#171A1E"));
                    SetSolidBrush(resources, "KroiraSurfaceElevatedBrush", Parse("#20252B"));
                    break;
                default:
                    SetGradientStops(resources, "KroiraPageAtmosphereBrush", Parse("#171A1F"), Parse("#090A0D"), Parse("#050607"));
                    SetGradientStops(resources, "KroiraShellGradientBrush", Parse("#17100A"), Parse("#090A0D"), Parse("#030405"));
                    SetGradientStops(resources, "KroiraMediaGradientBrush", Parse("#362514"), Parse("#161B22"), Parse("#090A0D"));
                    SetSolidBrush(resources, "KroiraSurfaceBrush", Parse("#151A22"));
                    SetSolidBrush(resources, "KroiraSurfaceElevatedBrush", Parse("#1B212A"));
                    break;
            }
        }

        private static void ApplyAccentPreset(ResourceDictionary resources, string key)
        {
            switch (key)
            {
                case "signal":
                    ApplyAccent(
                        resources,
                        accent: Parse("#79D7F0"),
                        accentStrong: Parse("#2EA8FF"),
                        accentContainer: Parse("#163243"),
                        chromeLine: Parse("#79D7F0"),
                        primaryGradientStart: Parse("#84E7FF"),
                        primaryGradientEnd: Parse("#2C78D7"),
                        selectedBackground: Parse("#1E3142"),
                        focus: Parse("#79D7F0"));
                    break;
                case "ember":
                    ApplyAccent(
                        resources,
                        accent: Parse("#E6A06A"),
                        accentStrong: Parse("#E46C3F"),
                        accentContainer: Parse("#422317"),
                        chromeLine: Parse("#E6A06A"),
                        primaryGradientStart: Parse("#F0B16E"),
                        primaryGradientEnd: Parse("#A84725"),
                        selectedBackground: Parse("#3A2317"),
                        focus: Parse("#E6A06A"));
                    break;
                default:
                    ApplyAccent(
                        resources,
                        accent: Parse("#E7C46E"),
                        accentStrong: Parse("#F1A63E"),
                        accentContainer: Parse("#342716"),
                        chromeLine: Parse("#C8A86A"),
                        primaryGradientStart: Parse("#F0C568"),
                        primaryGradientEnd: Parse("#A95B25"),
                        selectedBackground: Parse("#282015"),
                        focus: Parse("#E7C46E"));
                    break;
            }
        }

        private static void ApplyAccent(
            ResourceDictionary resources,
            Color accent,
            Color accentStrong,
            Color accentContainer,
            Color chromeLine,
            Color primaryGradientStart,
            Color primaryGradientEnd,
            Color selectedBackground,
            Color focus)
        {
            SetSolidBrush(resources, "KroiraAccentBrush", accent);
            SetSolidBrush(resources, "KroiraAccentStrongBrush", accentStrong);
            SetSolidBrush(resources, "KroiraAccentContainerBrush", accentContainer);
            SetSolidBrush(resources, "KroiraGoldBrush", accent);
            SetSolidBrush(resources, "KroiraChromeLineBrush", chromeLine, 0.34);
            SetGradientStops(resources, "KroiraAccentGradientBrush", primaryGradientStart, primaryGradientEnd);

            SetSolidBrush(resources, "NavigationViewSelectionIndicatorForeground", accent);
            SetSolidBrush(resources, "NavigationViewItemBackgroundSelected", selectedBackground);
            SetSolidBrush(resources, "NavigationViewItemBackgroundSelectedPointerOver", selectedBackground, 1);
            SetSolidBrush(resources, "GridViewItemFocusVisualPrimaryBrush", focus);
            SetSolidBrush(resources, "GridViewItemFocusBorderBrush", focus);
            SetSolidBrush(resources, "ListViewItemFocusVisualPrimaryBrush", focus);
            SetSolidBrush(resources, "ListViewItemFocusBorderBrush", focus);
            SetSolidBrush(resources, "GridViewItemBackgroundPointerOver", accent, 0.08);
            SetSolidBrush(resources, "GridViewItemBackgroundPressed", accent, 0.12);
            SetSolidBrush(resources, "GridViewItemBackgroundSelected", accent, 0.14);
            SetSolidBrush(resources, "GridViewItemBackgroundSelectedPointerOver", accent, 0.18);
            SetSolidBrush(resources, "GridViewItemBackgroundSelectedPressed", accent, 0.16);
            SetSolidBrush(resources, "ListViewItemBackgroundPointerOver", accent, 0.08);
            SetSolidBrush(resources, "ListViewItemBackgroundPressed", accent, 0.12);
            SetSolidBrush(resources, "ListViewItemBackgroundSelected", accent, 0.14);
            SetSolidBrush(resources, "ListViewItemBackgroundSelectedPointerOver", accent, 0.18);
            SetSolidBrush(resources, "ListViewItemBackgroundSelectedPressed", accent, 0.16);
        }

        private static void SetSolidBrush(ResourceDictionary resources, string key, Color color, double? opacity = null)
        {
            if (resources[key] is SolidColorBrush brush)
            {
                brush.Color = color;
                if (opacity.HasValue)
                {
                    brush.Opacity = opacity.Value;
                }
            }
        }

        private static void SetGradientStops(ResourceDictionary resources, string key, params Color[] colors)
        {
            if (resources[key] is not LinearGradientBrush brush || colors.Length == 0)
            {
                return;
            }

            var stopCount = Math.Min(brush.GradientStops.Count, colors.Length);
            for (var index = 0; index < stopCount; index++)
            {
                brush.GradientStops[index].Color = colors[index];
            }
        }

        private static Color Parse(string hex)
        {
            var value = hex.TrimStart('#');
            return value.Length switch
            {
                6 => ColorHelper.FromArgb(255,
                    Convert.ToByte(value.Substring(0, 2), 16),
                    Convert.ToByte(value.Substring(2, 2), 16),
                    Convert.ToByte(value.Substring(4, 2), 16)),
                8 => ColorHelper.FromArgb(
                    Convert.ToByte(value.Substring(0, 2), 16),
                    Convert.ToByte(value.Substring(2, 2), 16),
                    Convert.ToByte(value.Substring(4, 2), 16),
                    Convert.ToByte(value.Substring(6, 2), 16)),
                _ => Colors.Transparent
            };
        }
    }
}

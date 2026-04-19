#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace Kroira.App.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IEntitlementService _entitlementService;
        private readonly IServiceProvider _serviceProvider;
        private bool _isLoadingLanguage;
        private bool _isLoadingAppearance;

        public ObservableCollection<LanguageOptionViewModel> Languages { get; } = new()
        {
            new LanguageOptionViewModel(AppLanguageService.DefaultLanguageCode, "English")
        };

        public ObservableCollection<AppAppearanceOptionViewModel> ThemeOptions { get; } = new();
        public ObservableCollection<AppAppearanceOptionViewModel> AccentOptions { get; } = new();

        [ObservableProperty]
        private Visibility _freeTierVisibility;

        [ObservableProperty]
        private Visibility _proTierVisibility;

        [ObservableProperty]
        private string _licenseStatusDescription = string.Empty;

        [ObservableProperty]
        private LanguageOptionViewModel _selectedLanguage = new LanguageOptionViewModel(AppLanguageService.DefaultLanguageCode, "English");

        [ObservableProperty]
        private string _languageStatusText = "English is selected. This stable option uses the working catalog language pipeline.";

        [ObservableProperty]
        private AppAppearanceOptionViewModel? _selectedThemeOption;

        [ObservableProperty]
        private AppAppearanceOptionViewModel? _selectedAccentOption;

        [ObservableProperty]
        private string _appearanceStatusText = "Choose an appearance preset and accent pack. Entitlement gating can be added centrally later.";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBackupIdle))]
        [NotifyPropertyChangedFor(nameof(CanStartBackupAction))]
        private bool _isBackupBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBackupStatus))]
        private string _backupStatusText = "Export or restore a versioned local package for sources, profiles, favorites, watch state, and local preferences.";

        public bool IsBackupIdle => !IsBackupBusy;
        public bool CanUseBackupRestore => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.LibraryBackupRestore);
        public bool CanUseThemePresets => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.AppearanceThemes);
        public bool CanUseAccentPacks => _entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.AppearanceAccentPacks);
        public bool CanStartBackupAction => CanUseBackupRestore && !IsBackupBusy;
        public bool HasBackupStatus => !string.IsNullOrWhiteSpace(BackupStatusText);

        partial void OnSelectedLanguageChanged(LanguageOptionViewModel value)
        {
            if (!_isLoadingLanguage && value != null)
            {
                _ = SaveLanguageAsync(value.Code);
            }
        }

        partial void OnSelectedThemeOptionChanged(AppAppearanceOptionViewModel? value)
        {
            if (!_isLoadingAppearance && value != null)
            {
                _ = SaveAppearanceAsync();
            }
        }

        partial void OnSelectedAccentOptionChanged(AppAppearanceOptionViewModel? value)
        {
            if (!_isLoadingAppearance && value != null)
            {
                _ = SaveAppearanceAsync();
            }
        }

        public SettingsViewModel(IEntitlementService entitlementService, IServiceProvider serviceProvider)
        {
            _entitlementService = entitlementService;
            _serviceProvider = serviceProvider;
            var appearanceService = _serviceProvider.GetRequiredService<IAppAppearanceService>();
            foreach (var option in appearanceService.ThemeOptions)
            {
                ThemeOptions.Add(new AppAppearanceOptionViewModel(option.Key, option.DisplayName, option.Description));
            }

            foreach (var option in appearanceService.AccentOptions)
            {
                AccentOptions.Add(new AppAppearanceOptionViewModel(option.Key, option.DisplayName, option.Description));
            }

            UpdateState();
        }

        [RelayCommand]
        public async Task LoadSettingsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var appearanceService = scope.ServiceProvider.GetRequiredService<IAppAppearanceService>();
            var activeProfile = await profileService.GetActiveProfileAsync(db);
            var languageCode = await AppLanguageService.GetLanguageAsync(db, activeProfile.Id);
            await AppLanguageService.SetLanguageAsync(db, languageCode, activeProfile.Id);
            var appearance = await appearanceService.LoadAsync(db);

            _isLoadingLanguage = true;
            SelectedLanguage = Languages.FirstOrDefault(language => language.Code == languageCode)
                ?? Languages.First(language => language.Code == AppLanguageService.DefaultLanguageCode);
            _isLoadingLanguage = false;
            LanguageStatusText = $"{activeProfile.Name} uses the current language preference.";

            _isLoadingAppearance = true;
            SelectedThemeOption = ThemeOptions.FirstOrDefault(option => option.Key == appearance.ThemePresetKey) ?? ThemeOptions.FirstOrDefault();
            SelectedAccentOption = AccentOptions.FirstOrDefault(option => option.Key == appearance.AccentPresetKey) ?? AccentOptions.FirstOrDefault();
            _isLoadingAppearance = false;
            AppearanceStatusText = $"{SelectedThemeOption?.DisplayName ?? "Cinema Gold"} with {SelectedAccentOption?.DisplayName ?? "House Gold"} is active.";
        }

        private async Task SaveLanguageAsync(string languageCode)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfile = await profileService.GetActiveProfileAsync(db);
            await AppLanguageService.SetLanguageAsync(db, languageCode, activeProfile.Id);
            LanguageStatusText = $"{activeProfile.Name} now uses the selected language preference.";
        }

        private async Task SaveAppearanceAsync()
        {
            if ((SelectedThemeOption == null && SelectedAccentOption == null) ||
                (!CanUseThemePresets && !CanUseAccentPacks))
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appearanceService = scope.ServiceProvider.GetRequiredService<IAppAppearanceService>();
            var current = await appearanceService.LoadAsync(db);
            current.ThemePresetKey = CanUseThemePresets ? SelectedThemeOption?.Key ?? current.ThemePresetKey : current.ThemePresetKey;
            current.AccentPresetKey = CanUseAccentPacks ? SelectedAccentOption?.Key ?? current.AccentPresetKey : current.AccentPresetKey;
            await appearanceService.SaveAsync(db, current);

            AppearanceStatusText = $"{SelectedThemeOption?.DisplayName ?? "Cinema Gold"} with {SelectedAccentOption?.DisplayName ?? "House Gold"} is active.";
        }

        [RelayCommand]
        private async Task UpgradeAsync()
        {
            bool success = await _entitlementService.PurchaseProLicenseAsync();
            if (success)
            {
                UpdateState();
            }
        }

        private void UpdateState()
        {
            bool isPro = string.Equals(_entitlementService.CurrentTierKey, "pro", StringComparison.OrdinalIgnoreCase);
            ProTierVisibility = isPro ? Visibility.Visible : Visibility.Collapsed;
            FreeTierVisibility = !isPro ? Visibility.Visible : Visibility.Collapsed;

            var backupRestoreState = CanUseBackupRestore ? "enabled" : "disabled";
            var appearanceState = CanUseThemePresets || CanUseAccentPacks ? "ready" : "disabled";
            LicenseStatusDescription = $"{_entitlementService.CurrentTierDisplayName} tier is active. Central entitlement routing is ready; backup/restore is currently {backupRestoreState} and appearance presets are {appearanceState}.";
        }

        public async Task ExportBackupAsync(string filePath)
        {
            if (!CanUseBackupRestore)
            {
                BackupStatusText = "Backup export is unavailable for this entitlement.";
                return;
            }

            if (IsBackupBusy)
            {
                return;
            }

            IsBackupBusy = true;
            BackupStatusText = "Exporting backup package...";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IBackupPackageService>();
                var result = await backupService.ExportAsync(filePath);

                BackupStatusText =
                    $"Exported {result.SourceCount} sources, {result.ProfileCount} profiles, " +
                    $"{result.FavoriteCount} favorites, and {result.WatchStateCount} watch-state records.";
            }
            catch (Exception ex)
            {
                BackupStatusText = $"Backup export failed: {ex.Message}";
            }
            finally
            {
                IsBackupBusy = false;
            }
        }

        public async Task RestoreBackupAsync(string filePath)
        {
            if (!CanUseBackupRestore)
            {
                BackupStatusText = "Backup restore is unavailable for this entitlement.";
                return;
            }

            if (IsBackupBusy)
            {
                return;
            }

            IsBackupBusy = true;
            BackupStatusText = "Restoring backup package...";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IBackupPackageService>();
                var result = await backupService.RestoreAsync(filePath);
                await LoadSettingsAsync();

                var builder = new StringBuilder();
                builder.Append(
                    $"Restored {result.SourceCount} sources, {result.ProfileCount} profiles, " +
                    $"{result.FavoriteCount} favorites, and {result.WatchStateCount} watch-state records.");

                if (result.SourceSyncFailureCount > 0)
                {
                    builder.Append($" {result.SourceSyncFailureCount} sources need attention after re-import.");
                }

                if (result.Warnings.Count > 0)
                {
                    builder.Append($" {result.Warnings[0]}");
                }

                BackupStatusText = builder.ToString();
            }
            catch (Exception ex)
            {
                BackupStatusText = $"Backup restore failed: {ex.Message}";
            }
            finally
            {
                IsBackupBusy = false;
            }
        }

        public string AppVersion
        {
            get
            {
                try
                {
                    var version = Package.Current.Id.Version;
                    return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                }
                catch
                {
                    var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision} (Unpackaged)" : "1.0.0.0";
                }
            }
        }

        [RelayCommand]
        public async Task OpenPrivacyPolicyAsync()
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://sativagenetics.github.io/KroiraIPTV/privacy.html"));
        }

        [RelayCommand]
        public async Task OpenSupportAsync()
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://sativagenetics.github.io/KroiraIPTV/support.html"));
        }
    }

    public sealed class LanguageOptionViewModel
    {
        public LanguageOptionViewModel(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public string Code { get; }
        public string DisplayName { get; }
    }

    public sealed class AppAppearanceOptionViewModel
    {
        public AppAppearanceOptionViewModel(string key, string displayName, string description)
        {
            Key = key;
            DisplayName = displayName;
            Description = description;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
    }
}

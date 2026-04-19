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

        public ObservableCollection<LanguageOptionViewModel> Languages { get; } = new()
        {
            new LanguageOptionViewModel(AppLanguageService.DefaultLanguageCode, "English")
        };

        [ObservableProperty]
        private Visibility _freeTierVisibility;

        [ObservableProperty]
        private Visibility _proTierVisibility;

        [ObservableProperty]
        private string _licenseStatusDescription = string.Empty;

        [ObservableProperty]
        private LanguageOptionViewModel _selectedLanguage;

        [ObservableProperty]
        private string _languageStatusText = "English is selected. This stable option uses the working catalog language pipeline.";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBackupIdle))]
        private bool _isBackupBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBackupStatus))]
        private string _backupStatusText = "Export or restore a versioned local package for sources, profiles, favorites, watch state, and local preferences.";

        public bool IsBackupIdle => !IsBackupBusy;
        public bool HasBackupStatus => !string.IsNullOrWhiteSpace(BackupStatusText);

        partial void OnSelectedLanguageChanged(LanguageOptionViewModel value)
        {
            if (!_isLoadingLanguage && value != null)
            {
                _ = SaveLanguageAsync(value.Code);
            }
        }

        public SettingsViewModel(IEntitlementService entitlementService, IServiceProvider serviceProvider)
        {
            _entitlementService = entitlementService;
            _serviceProvider = serviceProvider;
            UpdateState();
        }

        [RelayCommand]
        public async Task LoadSettingsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfile = await profileService.GetActiveProfileAsync(db);
            var languageCode = await AppLanguageService.GetLanguageAsync(db, activeProfile.Id);
            await AppLanguageService.SetLanguageAsync(db, languageCode, activeProfile.Id);

            _isLoadingLanguage = true;
            SelectedLanguage = Languages.FirstOrDefault(language => language.Code == languageCode)
                ?? Languages.First(language => language.Code == AppLanguageService.DefaultLanguageCode);
            _isLoadingLanguage = false;
            LanguageStatusText = $"{activeProfile.Name} uses the current language preference.";
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
            bool isPro = _entitlementService.HasProLicense;
            ProTierVisibility = isPro ? Visibility.Visible : Visibility.Collapsed;
            FreeTierVisibility = !isPro ? Visibility.Visible : Visibility.Collapsed;

            LicenseStatusDescription = isPro
                ? "Pro features are enabled for this installation."
                : "Free tier is active. Upgrade to enable multi-monitor, recording, and premium playback features.";
        }

        public async Task ExportBackupAsync(string filePath)
        {
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
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
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
            new LanguageOptionViewModel(AppLanguageService.DefaultLanguageCode, "Türkçe"),
            new LanguageOptionViewModel("en-US", "English")
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
        private string _languageStatusText = "Türkçe is the default app language. English is available as the only secondary supported option.";

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
            var languageCode = await AppLanguageService.GetLanguageAsync(db);
            await AppLanguageService.SetLanguageAsync(db, languageCode);

            _isLoadingLanguage = true;
            SelectedLanguage = Languages.FirstOrDefault(language => language.Code == languageCode)
                ?? Languages.First(language => language.Code == AppLanguageService.DefaultLanguageCode);
            _isLoadingLanguage = false;
        }

        private async Task SaveLanguageAsync(string languageCode)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await AppLanguageService.SetLanguageAsync(db, languageCode);
            var normalizedLanguageCode = AppLanguageService.NormalizeLanguageCode(languageCode);
            LanguageStatusText = normalizedLanguageCode == AppLanguageService.DefaultLanguageCode
                ? "Türkçe selected. Unsupported language options remain hidden until real app support exists."
                : "English selected. Unsupported language options remain hidden until real app support exists.";
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

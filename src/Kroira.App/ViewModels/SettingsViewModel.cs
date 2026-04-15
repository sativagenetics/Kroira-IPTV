using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Services;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace Kroira.App.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IEntitlementService _entitlementService;

        [ObservableProperty]
        private Visibility _freeTierVisibility;

        [ObservableProperty]
        private Visibility _proTierVisibility;

        [ObservableProperty]
        private string _licenseStatusDescription = string.Empty;

        public SettingsViewModel(IEntitlementService entitlementService)
        {
            _entitlementService = entitlementService;
            UpdateState();
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
                ? "You are rocking the Pro version! All advanced multi-monitor, external player fallback, and family features are enabled." 
                : "You are currently on the Free tier. Upgrade to Pro for multi-monitor, continuous recording, and premium playback features.";
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
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://kroira.app/privacy"));
        }

        [RelayCommand]
        public async Task OpenSupportAsync()
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://kroira.app/support"));
        }
    }
}

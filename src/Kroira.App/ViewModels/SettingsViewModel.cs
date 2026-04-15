using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Services;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;

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
    }
}

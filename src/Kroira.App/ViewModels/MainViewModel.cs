using CommunityToolkit.Mvvm.ComponentModel;
using Kroira.App.Services;

namespace Kroira.App.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IEntitlementService _entitlementService;

        [ObservableProperty]
        private string _licenseStatusMessage;

        public MainViewModel(IEntitlementService entitlementService)
        {
            _entitlementService = entitlementService;

            LicenseStatusMessage = $"App Initialized - {_entitlementService.CurrentTierDisplayName} Tier";
        }
    }
}

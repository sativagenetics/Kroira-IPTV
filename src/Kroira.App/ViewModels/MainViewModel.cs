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
            
            LicenseStatusMessage = _entitlementService.HasProLicense 
                ? "App Initialized - Pro License Valid" 
                : "App Initialized - Operating in Free Tier";
        }
    }
}

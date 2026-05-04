using System.Threading.Tasks;

namespace Kroira.App.Services
{
    /// <summary>
    /// Stub entitlement service used until real Store IAP is wired up.
    /// Always reports Free tier; purchase calls are no-ops. StoreContext integration is outside the v2.0.1 release candidate scope.
    /// </summary>
    public class MockEntitlementService : IEntitlementService
    {
        public bool HasProLicense { get; private set; } = false;
        public string CurrentTierKey => HasProLicense ? "pro" : "free";
        public string CurrentTierDisplayName => HasProLicense ? "Pro" : "Free";

        public bool IsFeatureEnabled(string featureKey)
        {
            return true;
        }

        public int? GetLimit(string limitKey)
        {
            return null;
        }

        public Task<bool> RefreshLicenseAsync()
        {
            return Task.FromResult(HasProLicense);
        }

        public Task<bool> PurchaseProLicenseAsync()
        {
            // NOTE: No-op in this release. Real IAP via StoreContext not yet implemented.
            // HasProLicense intentionally NOT set to true here.
            return Task.FromResult(false);
        }
    }
}

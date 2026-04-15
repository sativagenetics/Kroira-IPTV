using System.Threading.Tasks;

namespace Kroira.App.Services
{
    /// <summary>
    /// Stub entitlement service used until real Store IAP is wired up.
    /// Always reports Free tier; purchase calls are no-ops.
    /// TODO (IAP): Replace with StoreContext-based implementation before monetisation launch.
    /// </summary>
    public class MockEntitlementService : IEntitlementService
    {
        public bool HasProLicense { get; private set; } = false;

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

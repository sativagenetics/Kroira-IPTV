using System.Threading.Tasks;

namespace Kroira.App.Services
{
    /// <summary>
    /// For Slice 1. Mocks out the licensing system to ensure DI correctly validates application structure.
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
            HasProLicense = true;
            return Task.FromResult(true);
        }
    }
}

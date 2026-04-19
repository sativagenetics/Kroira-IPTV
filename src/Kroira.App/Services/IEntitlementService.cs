using System.Threading.Tasks;

namespace Kroira.App.Services
{
    public interface IEntitlementService
    {
        bool HasProLicense { get; }
        string CurrentTierKey { get; }
        string CurrentTierDisplayName { get; }
        bool IsFeatureEnabled(string featureKey);
        int? GetLimit(string limitKey);
        Task<bool> RefreshLicenseAsync();
        Task<bool> PurchaseProLicenseAsync();
    }
}

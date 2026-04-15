using System.Threading.Tasks;

namespace Kroira.App.Services
{
    public interface IEntitlementService
    {
        bool HasProLicense { get; }
        Task<bool> RefreshLicenseAsync();
        Task<bool> PurchaseProLicenseAsync();
    }
}

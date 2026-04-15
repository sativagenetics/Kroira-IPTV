namespace Kroira.App.Models
{
    public class FeatureEntitlement
    {
        public int Id { get; set; }
        public string StoreSku { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string OfflineSignature { get; set; } = string.Empty;
    }
}

namespace Kroira.App.Models
{
    public class ParentalControlSetting
    {
        public int Id { get; set; }
        public int ProfileId { get; set; } = 1;
        public string PinHash { get; set; } = string.Empty;
        public string LockedCategoryIdsJson { get; set; } = string.Empty;
        public string LockedSourceIdsJson { get; set; } = string.Empty;
        public bool IsKidsSafeMode { get; set; }
        public bool HideLockedContent { get; set; } = true;
    }
}

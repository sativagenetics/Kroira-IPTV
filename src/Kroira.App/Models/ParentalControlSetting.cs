namespace Kroira.App.Models
{
    public class ParentalControlSetting
    {
        public int Id { get; set; }
        public string PinHash { get; set; } = string.Empty;
        public string LockedCategoryIdsJson { get; set; } = string.Empty;
    }
}

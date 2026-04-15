namespace Kroira.App.Models
{
    public class SourceCredential
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string EpgUrl { get; set; } = string.Empty;
    }
}

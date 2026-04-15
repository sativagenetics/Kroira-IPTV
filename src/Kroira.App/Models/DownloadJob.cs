namespace Kroira.App.Models
{
    public class DownloadJob
    {
        public int Id { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public int ContentId { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
    }
}

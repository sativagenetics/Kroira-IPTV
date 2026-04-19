namespace Kroira.App.Models
{
    public class PlaybackLaunchContext
    {
        public int ProfileId { get; set; }
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public string StreamUrl { get; set; } = string.Empty;
        public long StartPositionMs { get; set; }
    }
}

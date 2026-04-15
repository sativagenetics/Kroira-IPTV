using System;

namespace Kroira.App.Models
{
    public enum PlaybackContentType { Channel, Movie, Episode }

    public class PlaybackProgress
    {
        public int Id { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public int ContentId { get; set; }
        public long PositionMs { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime LastWatched { get; set; }
    }
}

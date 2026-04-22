using System;

namespace Kroira.App.Models
{
    public enum PlaybackContentType { Channel, Movie, Episode }
    public enum WatchStateOverride { None, Watched, Unwatched }

    public class PlaybackProgress
    {
        public int Id { get; set; }
        public int ProfileId { get; set; } = 1;
        public PlaybackContentType ContentType { get; set; }
        public int ContentId { get; set; }
        public string LogicalContentKey { get; set; } = string.Empty;
        public int PreferredSourceProfileId { get; set; }
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
        public bool IsCompleted { get; set; }
        public WatchStateOverride WatchStateOverride { get; set; }
        public DateTime LastWatched { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
    }
}

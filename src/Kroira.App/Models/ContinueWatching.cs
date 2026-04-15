using Microsoft.EntityFrameworkCore;
using System;

namespace Kroira.App.Models
{
    [Keyless]
    public class ContinueWatching
    {
        public PlaybackContentType ContentType { get; set; }
        public int ContentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public long PositionMs { get; set; }
        public DateTime LastWatched { get; set; }
    }
}

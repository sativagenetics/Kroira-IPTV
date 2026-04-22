using System;

namespace Kroira.App.Models
{
    public enum FavoriteType { Channel, Movie, Series }

    public class Favorite
    {
        public int Id { get; set; }
        public int ProfileId { get; set; } = 1;
        public FavoriteType ContentType { get; set; }
        public int ContentId { get; set; }
        public string LogicalContentKey { get; set; } = string.Empty;
        public int PreferredSourceProfileId { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
    }
}

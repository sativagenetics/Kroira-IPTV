#nullable enable
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public class Series
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        /// <summary>Xtream series_id — stable identity key used for upsert across syncs.</summary>
        public string ExternalId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string TmdbId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;

        public ICollection<Season>? Seasons { get; set; }
    }
}

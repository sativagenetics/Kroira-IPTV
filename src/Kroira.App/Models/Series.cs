using System.Collections.Generic;

namespace Kroira.App.Models
{
    public class Series
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string TmdbId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;

        public ICollection<Season>? Seasons { get; set; }
    }
}

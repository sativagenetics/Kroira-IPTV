using System.Collections.Generic;

namespace Kroira.App.Models
{
    public class Season
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int SeasonNumber { get; set; }
        public string PosterUrl { get; set; } = string.Empty;

        public ICollection<Episode>? Episodes { get; set; }
    }
}

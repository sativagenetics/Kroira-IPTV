namespace Kroira.App.Models
{
    public class Movie
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        /// <summary>Xtream stream_id — stable identity key used for upsert across syncs.</summary>
        public string ExternalId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string TmdbId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
    }
}

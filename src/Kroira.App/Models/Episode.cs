namespace Kroira.App.Models
{
    public class Episode
    {
        public int Id { get; set; }
        public int SeasonId { get; set; }
        /// <summary>Xtream episode id — stable identity key used for upsert across syncs.</summary>
        public string ExternalId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public int EpisodeNumber { get; set; }
    }
}

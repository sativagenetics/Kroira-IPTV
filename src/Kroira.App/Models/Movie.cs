namespace Kroira.App.Models
{
    public class Movie
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string TmdbId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
    }
}

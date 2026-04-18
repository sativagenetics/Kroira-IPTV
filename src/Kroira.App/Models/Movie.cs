using System;
using System.Linq;

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
        public string ImdbId { get; set; } = string.Empty;
        public string TmdbPosterPath { get; set; } = string.Empty;
        public string TmdbBackdropPath { get; set; } = string.Empty;
        public string BackdropUrl { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public string Genres { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; }
        public double VoteAverage { get; set; }
        public double Popularity { get; set; }
        public string OriginalLanguage { get; set; } = string.Empty;
        public DateTime? MetadataUpdatedAt { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        public string DisplayYear => ReleaseDate.HasValue ? ReleaseDate.Value.Year.ToString() : string.Empty;

        public string RatingText => VoteAverage > 0 ? VoteAverage.ToString("0.0") : string.Empty;

        public string MetadataLine
        {
            get
            {
                var parts = new[]
                {
                    DisplayYear,
                    string.IsNullOrWhiteSpace(Genres) ? CategoryName : Genres,
                    string.IsNullOrWhiteSpace(OriginalLanguage) ? string.Empty : OriginalLanguage.ToUpperInvariant()
                };

                return string.Join(" / ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            }
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kroira.App.Models
{
    public class Series
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        /// <summary>Xtream series_id — stable identity key used for upsert across syncs.</summary>
        public string ExternalId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string RawSourceTitle { get; set; } = string.Empty;
        public string CanonicalTitleKey { get; set; } = string.Empty;
        public string DedupFingerprint { get; set; } = string.Empty;
        public string PosterUrl { get; set; } = string.Empty;
        public string TmdbId { get; set; } = string.Empty;
        public string ImdbId { get; set; } = string.Empty;
        public string TmdbPosterPath { get; set; } = string.Empty;
        public string TmdbBackdropPath { get; set; } = string.Empty;
        public string BackdropUrl { get; set; } = string.Empty;
        public string Overview { get; set; } = string.Empty;
        public string Genres { get; set; } = string.Empty;
        public DateTime? FirstAirDate { get; set; }
        public double VoteAverage { get; set; }
        public double Popularity { get; set; }
        public string OriginalLanguage { get; set; } = string.Empty;
        public DateTime? MetadataUpdatedAt { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string RawSourceCategoryName { get; set; } = string.Empty;
        public string ContentKind { get; set; } = "Primary";

        public ICollection<Season>? Seasons { get; set; }

        public string DisplayYear => FirstAirDate.HasValue ? FirstAirDate.Value.Year.ToString() : string.Empty;

        public string RatingText => VoteAverage > 0 ? VoteAverage.ToString("0.0") : string.Empty;

        public string DisplayPosterUrl => !string.IsNullOrWhiteSpace(PosterUrl)
            ? PosterUrl
            : BuildTmdbImageUrl(TmdbPosterPath, "w500");

        public string DisplayBackdropUrl => !string.IsNullOrWhiteSpace(BackdropUrl)
            ? BackdropUrl
            : BuildTmdbImageUrl(TmdbBackdropPath, "w1280");

        public string DisplayHeroArtworkUrl => !string.IsNullOrWhiteSpace(DisplayBackdropUrl)
            ? DisplayBackdropUrl
            : DisplayPosterUrl;

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

        private static string BuildTmdbImageUrl(string path, string size)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : $"https://image.tmdb.org/t/p/{size}{path}";
        }
    }
}

#nullable enable
using System;
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

        public ICollection<Season>? Seasons { get; set; }
    }
}

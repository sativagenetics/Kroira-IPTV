#nullable enable
using System;

namespace Kroira.App.Models
{
    public enum SmartCategoryMediaType
    {
        Live = 0,
        Movie = 1,
        Series = 2
    }

    public sealed class SmartCategoryItemContext
    {
        public SmartCategoryMediaType MediaType { get; init; }
        public string Title { get; init; } = string.Empty;
        public string RawTitle { get; init; } = string.Empty;
        public string ProviderGroupName { get; init; } = string.Empty;
        public string DisplayCategoryName { get; init; } = string.Empty;
        public string Genres { get; init; } = string.Empty;
        public string OriginalLanguage { get; init; } = string.Empty;
        public string SourceName { get; init; } = string.Empty;
        public string SourceSummary { get; init; } = string.Empty;
        public string TvgId { get; init; } = string.Empty;
        public string TvgName { get; init; } = string.Empty;
        public string LogoUrl { get; init; } = string.Empty;
        public string EpgCurrentTitle { get; init; } = string.Empty;
        public string EpgNextTitle { get; init; } = string.Empty;
        public DateTime? ReleaseDate { get; init; }
        public DateTime? SourceLastSyncUtc { get; init; }
        public double VoteAverage { get; init; }
        public double Popularity { get; init; }
        public int VariantCount { get; init; }
        public int SeasonCount { get; init; }
        public int EpisodeCount { get; init; }
        public bool IsFavorite { get; init; }
        public bool IsRecentlyWatched { get; init; }
        public bool IsContinueWatching { get; init; }
        public bool IsWatched { get; init; }
        public bool IsInProgress { get; init; }
        public bool IsCompleted { get; init; }
        public bool HasMetadata { get; init; }
        public bool HasArtwork { get; init; }
        public bool HasGuideLink { get; init; }
        public bool HasGuideData { get; init; }
        public bool HasMatchedGuide { get; init; }
        public bool IsPrimary { get; init; } = true;
        public bool HasMissingEpisodes { get; init; }
        public bool HasCompleteSeasons { get; init; }
    }

    public sealed class SmartCategoryDefinition
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string SectionName { get; init; }
        public required SmartCategoryMediaType MediaType { get; init; }
        public string IconGlyph { get; init; } = "\uE8B2";
        public string MatchRule { get; init; } = string.Empty;
        public int SortPriority { get; init; }
        public bool AlwaysShow { get; init; }
        public bool IsAllCategory { get; init; }
        public bool IsOriginalProviderGroupFallback { get; init; }
        public required Func<SmartCategoryItemContext, bool> Predicate { get; init; }
    }
}

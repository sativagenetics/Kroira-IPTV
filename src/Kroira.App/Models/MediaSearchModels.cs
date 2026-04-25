#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kroira.App.Models
{
    public enum MediaSearchResultType
    {
        Live = 0,
        Movie = 1,
        Series = 2,
        Episode = 3
    }

    public sealed class MediaSearchResponse
    {
        public MediaSearchResponse(
            string query,
            string normalizedQuery,
            IReadOnlyList<MediaSearchResultGroup> groups,
            bool isEmptyQuery)
        {
            Query = query;
            NormalizedQuery = normalizedQuery;
            Groups = groups;
            IsEmptyQuery = isEmptyQuery;
        }

        public string Query { get; }
        public string NormalizedQuery { get; }
        public IReadOnlyList<MediaSearchResultGroup> Groups { get; }
        public bool IsEmptyQuery { get; }
        public int TotalCount => Groups.Sum(group => group.Results.Count);
    }

    public sealed class MediaSearchResultGroup
    {
        public MediaSearchResultGroup(
            MediaSearchResultType type,
            string heading,
            IReadOnlyList<MediaSearchResult> results)
        {
            Type = type;
            Heading = heading;
            Results = results;
        }

        public MediaSearchResultType Type { get; }
        public string Heading { get; }
        public IReadOnlyList<MediaSearchResult> Results { get; }
    }

    public sealed class MediaSearchResult
    {
        public MediaSearchResultType Type { get; init; }
        public int ContentId { get; init; }
        public PlaybackContentType? PlaybackContentType { get; init; }
        public int SourceProfileId { get; init; }
        public int? SeriesId { get; init; }
        public int? SeasonId { get; init; }
        public int? SeasonNumber { get; init; }
        public int? EpisodeNumber { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string Overview { get; init; } = string.Empty;
        public string SourceName { get; init; } = string.Empty;
        public string SourceBadge { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public string CategoryBadge { get; init; } = string.Empty;
        public string ArtworkUrl { get; init; } = string.Empty;
        public string StreamUrl { get; init; } = string.Empty;
        public string LogicalContentKey { get; init; } = string.Empty;
        public long ResumePositionMs { get; init; }
        public int RelevanceScore { get; init; }
    }
}

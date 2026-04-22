#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public enum CatalogDiscoveryDomain
    {
        Live = 0,
        Movies = 1,
        Series = 2
    }

    public enum CatalogDiscoveryHealthBucket
    {
        Unknown = 0,
        Healthy = 1,
        Attention = 2,
        Degraded = 3
    }

    public sealed class CatalogDiscoveryTag
    {
        public string Key { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
    }

    public sealed class CatalogDiscoveryRecord
    {
        public string Key { get; init; } = string.Empty;
        public CatalogDiscoveryDomain Domain { get; init; }
        public IReadOnlyList<int> SourceProfileIds { get; init; } = Array.Empty<int>();
        public IReadOnlyList<SourceType> SourceTypes { get; init; } = Array.Empty<SourceType>();
        public string LanguageKey { get; init; } = string.Empty;
        public string LanguageLabel { get; init; } = string.Empty;
        public IReadOnlyList<CatalogDiscoveryTag> Tags { get; init; } = Array.Empty<CatalogDiscoveryTag>();
        public bool IsFavorite { get; init; }
        public bool HasGuide { get; init; }
        public bool HasCatchup { get; init; }
        public bool HasArtwork { get; init; }
        public bool HasPlayableChildren { get; init; }
        public CatalogDiscoveryHealthBucket HealthBucket { get; init; }
        public DateTime? LastSyncUtc { get; init; }
        public DateTime? LastInteractionUtc { get; init; }
    }

    public sealed class CatalogDiscoverySelection
    {
        public string SignalKey { get; init; } = string.Empty;
        public string SourceTypeKey { get; init; } = string.Empty;
        public string LanguageKey { get; init; } = string.Empty;
        public string TagKey { get; init; } = string.Empty;
    }

    public sealed class CatalogDiscoveryFacetOption
    {
        public string Key { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public int ItemCount { get; init; }
    }

    public sealed class CatalogDiscoveryProjection
    {
        public CatalogDiscoverySelection EffectiveSelection { get; init; } = new();
        public IReadOnlyList<CatalogDiscoveryFacetOption> SignalOptions { get; init; } = Array.Empty<CatalogDiscoveryFacetOption>();
        public IReadOnlyList<CatalogDiscoveryFacetOption> SourceTypeOptions { get; init; } = Array.Empty<CatalogDiscoveryFacetOption>();
        public IReadOnlyList<CatalogDiscoveryFacetOption> LanguageOptions { get; init; } = Array.Empty<CatalogDiscoveryFacetOption>();
        public IReadOnlyList<CatalogDiscoveryFacetOption> TagOptions { get; init; } = Array.Empty<CatalogDiscoveryFacetOption>();
        public IReadOnlySet<string> MatchingKeys { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool HasActiveFacetFilters { get; init; }
        public int MatchingCount { get; init; }
        public int ProviderCount { get; init; }
        public string SummaryText { get; init; } = string.Empty;
    }
}

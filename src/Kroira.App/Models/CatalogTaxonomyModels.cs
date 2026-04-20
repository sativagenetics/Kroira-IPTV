#nullable enable
namespace Kroira.App.Models
{
    public enum CatalogTaxonomyDomain
    {
        Live = 0,
        Movies = 1,
        Series = 2
    }

    public enum CatalogTaxonomySignal
    {
        None = 0,
        Generic = 1,
        Genre = 2,
        LocalLanguage = 3,
        ForeignLanguage = 4,
        Platform = 5,
        Collection = 6,
        Documentary = 7,
        Kids = 8,
        Adult = 9,
        Sports = 10,
        News = 11,
        MovieChannels = 12,
        Entertainment = 13,
        Music = 14,
        International = 15,
        Other = 16
    }

    public sealed class CatalogTaxonomyResult
    {
        public CatalogTaxonomyDomain Domain { get; init; }
        public string RawCategoryName { get; init; } = string.Empty;
        public string NormalizedSourceCategoryName { get; init; } = string.Empty;
        public string DisplayCategoryName { get; init; } = string.Empty;
        public CatalogTaxonomySignal PrimarySignal { get; init; } = CatalogTaxonomySignal.None;
        public string AppliedRule { get; init; } = string.Empty;
        public bool IsPseudoBucket { get; init; }
        public bool IsLowSignal { get; init; }
        public bool IsPlatformBucket { get; init; }
        public bool IsCollectionBucket { get; init; }
        public bool IsYearBucket { get; init; }
        public bool IsAdult { get; init; }
    }

    public sealed class LiveChannelPresentationResult
    {
        public string RawName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string NormalizedName { get; init; } = string.Empty;
        public bool HadVariantNoise { get; init; }
    }
}

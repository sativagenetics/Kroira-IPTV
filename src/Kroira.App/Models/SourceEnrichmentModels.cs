#nullable enable
using System;

namespace Kroira.App.Models
{
    public enum ChannelEpgMatchSource
    {
        None = 0,
        Provider = 1,
        Previous = 2,
        Normalized = 3,
        Alias = 4,
        Fuzzy = 5,
        Regex = 6
    }

    public enum ChannelLogoSource
    {
        None = 0,
        Provider = 1,
        Previous = 2,
        Xmltv = 3
    }

    public class SourceChannelEnrichmentRecord
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string IdentityKey { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        public string AliasKeys { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string ProviderEpgChannelId { get; set; } = string.Empty;
        public string ProviderLogoUrl { get; set; } = string.Empty;
        public string ResolvedLogoUrl { get; set; } = string.Empty;
        public string MatchedXmltvChannelId { get; set; } = string.Empty;
        public string MatchedXmltvDisplayName { get; set; } = string.Empty;
        public string MatchedXmltvIconUrl { get; set; } = string.Empty;
        public ChannelEpgMatchSource EpgMatchSource { get; set; }
        public int EpgMatchConfidence { get; set; }
        public string EpgMatchSummary { get; set; } = string.Empty;
        public ChannelLogoSource LogoSource { get; set; }
        public int LogoConfidence { get; set; }
        public string LogoSummary { get; set; } = string.Empty;
        public DateTime LastAppliedAtUtc { get; set; }
        public DateTime LastSeenAtUtc { get; set; }
    }
}

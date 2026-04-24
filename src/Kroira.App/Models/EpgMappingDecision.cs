#nullable enable
using System;

namespace Kroira.App.Models
{
    public enum EpgMappingDecisionState
    {
        None = 0,
        Approved = 1,
        Rejected = 2
    }

    public class EpgMappingDecision
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public int ChannelId { get; set; }
        public string ChannelIdentityKey { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string ProviderEpgChannelId { get; set; } = string.Empty;
        public string StreamUrlHash { get; set; } = string.Empty;
        public string XmltvChannelId { get; set; } = string.Empty;
        public string XmltvDisplayName { get; set; } = string.Empty;
        public EpgMappingDecisionState Decision { get; set; }
        public ChannelEpgMatchSource SuggestedMatchSource { get; set; }
        public int SuggestedConfidence { get; set; }
        public string ReasonSummary { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}

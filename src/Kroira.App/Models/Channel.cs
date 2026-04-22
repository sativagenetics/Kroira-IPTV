#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public enum ChannelCatchupSource
    {
        None = 0,
        XtreamArchive = 1,
        M3uAttributes = 2,
        UrlPattern = 3,
        EpgWindow = 4
    }

    public class Channel
    {
        public int Id { get; set; }
        public int ChannelCategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string EpgChannelId { get; set; } = string.Empty;
        public string ProviderLogoUrl { get; set; } = string.Empty;
        public string ProviderEpgChannelId { get; set; } = string.Empty;
        public string NormalizedIdentityKey { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        public string AliasKeys { get; set; } = string.Empty;
        public ChannelEpgMatchSource EpgMatchSource { get; set; }
        public int EpgMatchConfidence { get; set; }
        public string EpgMatchSummary { get; set; } = string.Empty;
        public ChannelLogoSource LogoSource { get; set; }
        public int LogoConfidence { get; set; }
        public string LogoSummary { get; set; } = string.Empty;
        public string ProviderCatchupMode { get; set; } = string.Empty;
        public string ProviderCatchupSource { get; set; } = string.Empty;
        public bool SupportsCatchup { get; set; }
        public int CatchupWindowHours { get; set; }
        public ChannelCatchupSource CatchupSource { get; set; }
        public int CatchupConfidence { get; set; }
        public string CatchupSummary { get; set; } = string.Empty;
        public DateTime? CatchupDetectedAtUtc { get; set; }
        public DateTime? EnrichedAtUtc { get; set; }

        public ICollection<EpgProgram>? EpgPrograms { get; set; }
    }
}

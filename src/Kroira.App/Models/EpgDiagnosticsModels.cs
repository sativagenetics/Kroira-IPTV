#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public enum EpgGuideSourceKind
    {
        Provider = 0,
        Manual = 1,
        Custom = 2,
        Public = 3
    }

    public enum EpgGuideSourceStatus
    {
        Pending = 0,
        Ready = 1,
        Failed = 2,
        Skipped = 3
    }

    public sealed class EpgGuideSourceStatusSnapshot
    {
        public string Label { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public EpgGuideSourceKind Kind { get; set; }
        public EpgGuideSourceStatus Status { get; set; }
        public bool IsOptional { get; set; }
        public int Priority { get; set; }
        public int XmltvChannelCount { get; set; }
        public int ProgrammeCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime? CheckedAtUtc { get; set; }
        public long FetchDurationMs { get; set; }
        public bool WasCacheHit { get; set; }
        public bool WasContentUnchanged { get; set; }
        public string ContentHash { get; set; } = string.Empty;
    }

    public sealed class EpgCoverageReport
    {
        public int TotalLiveChannels { get; set; }
        public int ChannelsWithGuideProgrammes { get; set; }
        public int ExactMatches { get; set; }
        public int NormalizedMatches { get; set; }
        public int WeakMatches { get; set; }
        public int UnmatchedChannels { get; set; }
        public int ProgrammeCount { get; set; }
        public int XmltvChannelCount { get; set; }
        public DateTime? LastSyncAttemptUtc { get; set; }
        public DateTime? LastSuccessUtc { get; set; }
        public IReadOnlyList<EpgSourceCoverageReportItem> Sources { get; set; } = Array.Empty<EpgSourceCoverageReportItem>();
        public IReadOnlyList<EpgChannelCoverageReportItem> TopUnmatchedChannels { get; set; } = Array.Empty<EpgChannelCoverageReportItem>();
        public IReadOnlyList<EpgChannelCoverageReportItem> TopWeakMatches { get; set; } = Array.Empty<EpgChannelCoverageReportItem>();
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    }

    public sealed class EpgSourceCoverageReportItem
    {
        public int SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public SourceType SourceType { get; set; }
        public EpgActiveMode ActiveMode { get; set; } = EpgActiveMode.Detected;
        public EpgStatus Status { get; set; }
        public EpgSyncResultCode ResultCode { get; set; }
        public int TotalLiveChannels { get; set; }
        public int ChannelsWithGuideProgrammes { get; set; }
        public int ExactMatches { get; set; }
        public int NormalizedMatches { get; set; }
        public int WeakMatches { get; set; }
        public int UnmatchedChannels { get; set; }
        public int ProgrammeCount { get; set; }
        public int XmltvChannelCount { get; set; }
        public DateTime? LastSyncAttemptUtc { get; set; }
        public DateTime? LastSuccessUtc { get; set; }
        public string ActiveXmltvUrl { get; set; } = string.Empty;
        public string DetectedEpgUrl { get; set; } = string.Empty;
        public string ManualEpgUrl { get; set; } = string.Empty;
        public string FallbackEpgUrls { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
        public string WarningSummary { get; set; } = string.Empty;
        public IReadOnlyList<EpgGuideSourceStatusSnapshot> GuideSources { get; set; } = Array.Empty<EpgGuideSourceStatusSnapshot>();
        public bool CanSync { get; set; }
    }

    public sealed class EpgChannelCoverageReportItem
    {
        public int ChannelId { get; set; }
        public int SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string ProviderEpgChannelId { get; set; } = string.Empty;
        public string MatchedXmltvChannelId { get; set; } = string.Empty;
        public string MatchType { get; set; } = string.Empty;
        public int MatchConfidence { get; set; }
        public string MatchSummary { get; set; } = string.Empty;
        public int ProgrammeCount { get; set; }
        public bool IsActiveGuideAssignment { get; set; }
        public string ReviewStatus { get; set; } = string.Empty;
    }
}

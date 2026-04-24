using System;

namespace Kroira.App.Models
{
    public class EpgSyncLog
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public DateTime SyncedAtUtc { get; set; }
        public DateTime? LastSuccessAtUtc { get; set; }
        public bool IsSuccess { get; set; }
        public EpgStatus Status { get; set; }
        public EpgSyncResultCode ResultCode { get; set; }
        public EpgFailureStage FailureStage { get; set; }
        public EpgActiveMode ActiveMode { get; set; }
        public string ActiveXmltvUrl { get; set; } = string.Empty;
        public int MatchedChannelCount { get; set; }
        public int UnmatchedChannelCount { get; set; }
        public int CurrentCoverageCount { get; set; }
        public int NextCoverageCount { get; set; }
        public int TotalLiveChannelCount { get; set; }
        public int ProgrammeCount { get; set; }
        public int XmltvChannelCount { get; set; }
        public int ExactMatchCount { get; set; }
        public int NormalizedMatchCount { get; set; }
        public int WeakMatchCount { get; set; }
        public string MatchBreakdown { get; set; } = string.Empty;
        public string GuideSourceStatusJson { get; set; } = string.Empty;
        public string GuideWarningSummary { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
    }
}

#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public enum SourceAcquisitionRunStatus
    {
        Running = 0,
        Succeeded = 1,
        Failed = 2,
        Partial = 3,
        Backfilled = 4
    }

    public enum SourceAcquisitionStage
    {
        Acquire = 0,
        GuideMatch = 1,
        Validation = 2,
        RuntimeRepair = 3
    }

    public enum SourceAcquisitionOutcome
    {
        Suppressed = 0,
        Demoted = 1,
        Matched = 2,
        Unmatched = 3,
        Warning = 4,
        Failure = 5,
        Backfilled = 6
    }

    public enum SourceAcquisitionItemKind
    {
        LiveChannel = 0,
        Movie = 1,
        Series = 2,
        Episode = 3,
        XmltvChannel = 4,
        Probe = 5,
        Source = 6
    }

    public class SourceAcquisitionProfile
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string ProfileKey { get; set; } = string.Empty;
        public string ProfileLabel { get; set; } = string.Empty;
        public string ProviderKey { get; set; } = string.Empty;
        public string NormalizationSummary { get; set; } = string.Empty;
        public string MatchingSummary { get; set; } = string.Empty;
        public string SuppressionSummary { get; set; } = string.Empty;
        public string ValidationSummary { get; set; } = string.Empty;
        public bool SupportsRegexMatching { get; set; }
        public bool PreferProxyDuringValidation { get; set; }
        public bool PreferLastKnownGoodRollback { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class SourceAcquisitionRun
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public SourceRefreshTrigger Trigger { get; set; }
        public SourceRefreshScope Scope { get; set; }
        public SourceAcquisitionRunStatus Status { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string ProfileKey { get; set; } = string.Empty;
        public string ProfileLabel { get; set; } = string.Empty;
        public string ProviderKey { get; set; } = string.Empty;
        public string RoutingSummary { get; set; } = string.Empty;
        public string ValidationRoutingSummary { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string CatalogSummary { get; set; } = string.Empty;
        public string GuideSummary { get; set; } = string.Empty;
        public string ValidationSummary { get; set; } = string.Empty;
        public int RawItemCount { get; set; }
        public int AcceptedCount { get; set; }
        public int SuppressedCount { get; set; }
        public int DemotedCount { get; set; }
        public int MatchedCount { get; set; }
        public int UnmatchedCount { get; set; }
        public int LiveCount { get; set; }
        public int MovieCount { get; set; }
        public int SeriesCount { get; set; }
        public int EpisodeCount { get; set; }
        public int AliasMatchCount { get; set; }
        public int RegexMatchCount { get; set; }
        public int FuzzyMatchCount { get; set; }
        public int ProbeSuccessCount { get; set; }
        public int ProbeFailureCount { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }

        public ICollection<SourceAcquisitionEvidence> Evidence { get; set; } = new List<SourceAcquisitionEvidence>();
    }

    public class SourceAcquisitionEvidence
    {
        public int Id { get; set; }
        public int SourceAcquisitionRunId { get; set; }
        public int SourceProfileId { get; set; }
        public SourceAcquisitionStage Stage { get; set; }
        public SourceAcquisitionOutcome Outcome { get; set; }
        public SourceAcquisitionItemKind ItemKind { get; set; }
        public string RuleCode { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string RawName { get; set; } = string.Empty;
        public string RawCategory { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        public string NormalizedCategory { get; set; } = string.Empty;
        public string IdentityKey { get; set; } = string.Empty;
        public string AliasKeys { get; set; } = string.Empty;
        public string MatchedValue { get; set; } = string.Empty;
        public string MatchedTarget { get; set; } = string.Empty;
        public int Confidence { get; set; }
        public int SortOrder { get; set; }

        public SourceAcquisitionRun? Run { get; set; }
    }
}

#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public enum SourceHealthState
    {
        NotSynced = 0,
        Healthy = 1,
        Weak = 2,
        Incomplete = 3,
        Outdated = 4,
        Problematic = 5,
        Good = 6,
        Unknown = 7
    }

    public enum SourceHealthIssueSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public enum SourceHealthComponentType
    {
        Catalog = 0,
        Live = 1,
        Vod = 2,
        Epg = 3,
        Logos = 4,
        Freshness = 5
    }

    public enum SourceHealthComponentState
    {
        NotApplicable = 0,
        NotSynced = 1,
        Healthy = 2,
        Weak = 3,
        Incomplete = 4,
        Outdated = 5,
        Problematic = 6
    }

    public enum SourceHealthProbeType
    {
        Live = 0,
        Vod = 1
    }

    public enum SourceHealthProbeStatus
    {
        NotRun = 0,
        Completed = 1,
        Skipped = 2
    }

    public enum SourceRecommendedActionType
    {
        ResyncSource = 0,
        ConfigureEpg = 1,
        OpenManualEpgMatch = 2,
        RefreshMetadata = 3,
        RunStreamProbe = 4,
        ExportDiagnostics = 5,
        RemoveSource = 6
    }

    public sealed class SourceDiagnosticsMetricSnapshot
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public SourceActivityTone Tone { get; set; } = SourceActivityTone.Neutral;
    }

    public sealed class SourceRecommendedActionSnapshot
    {
        public SourceRecommendedActionType ActionType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string ButtonText { get; set; } = string.Empty;
        public SourceActivityTone Tone { get; set; } = SourceActivityTone.Neutral;
        public bool IsPrimary { get; set; }
        public int SortOrder { get; set; }
    }

    public class SourceHealthReport
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public DateTime EvaluatedAtUtc { get; set; }
        public DateTime? LastSyncAttemptAtUtc { get; set; }
        public DateTime? LastSuccessfulSyncAtUtc { get; set; }
        public int HealthScore { get; set; }
        public SourceHealthState HealthState { get; set; }
        public string StatusSummary { get; set; } = string.Empty;
        public string ImportResultSummary { get; set; } = string.Empty;
        public string ValidationSummary { get; set; } = string.Empty;
        public string TopIssueSummary { get; set; } = string.Empty;
        public int TotalChannelCount { get; set; }
        public int TotalMovieCount { get; set; }
        public int TotalSeriesCount { get; set; }
        public int DuplicateCount { get; set; }
        public int InvalidStreamCount { get; set; }
        public int ChannelsWithEpgMatchCount { get; set; }
        public int ChannelsWithCurrentProgramCount { get; set; }
        public int ChannelsWithNextProgramCount { get; set; }
        public int ChannelsWithLogoCount { get; set; }
        public int SuspiciousEntryCount { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }

        public ICollection<SourceHealthComponent> Components { get; set; } = new List<SourceHealthComponent>();
        public ICollection<SourceHealthProbe> Probes { get; set; } = new List<SourceHealthProbe>();
        public ICollection<SourceHealthIssue> Issues { get; set; } = new List<SourceHealthIssue>();
    }

    public class SourceHealthComponent
    {
        public int Id { get; set; }
        public int SourceHealthReportId { get; set; }
        public SourceHealthComponentType ComponentType { get; set; }
        public SourceHealthComponentState State { get; set; }
        public int Score { get; set; }
        public string Summary { get; set; } = string.Empty;
        public int RelevantCount { get; set; }
        public int HealthyCount { get; set; }
        public int IssueCount { get; set; }
        public int SortOrder { get; set; }

        public SourceHealthReport? Report { get; set; }
    }

    public class SourceHealthProbe
    {
        public int Id { get; set; }
        public int SourceHealthReportId { get; set; }
        public SourceHealthProbeType ProbeType { get; set; }
        public SourceHealthProbeStatus Status { get; set; }
        public DateTime? ProbedAtUtc { get; set; }
        public int CandidateCount { get; set; }
        public int SampleSize { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int TimeoutCount { get; set; }
        public int HttpErrorCount { get; set; }
        public int TransportErrorCount { get; set; }
        public string Summary { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        public SourceHealthReport? Report { get; set; }
    }

    public class SourceHealthIssue
    {
        public int Id { get; set; }
        public int SourceHealthReportId { get; set; }
        public SourceHealthIssueSeverity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int AffectedCount { get; set; }
        public string SampleItems { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        public SourceHealthReport? Report { get; set; }
    }

    public static class SourceHealthDisplay
    {
        public static string GetComponentLabel(SourceHealthComponentType componentType) => componentType switch
        {
            SourceHealthComponentType.Catalog => "Catalog",
            SourceHealthComponentType.Live => "Live",
            SourceHealthComponentType.Vod => "VOD",
            SourceHealthComponentType.Epg => "EPG",
            SourceHealthComponentType.Logos => "Logos",
            SourceHealthComponentType.Freshness => "Freshness",
            _ => "Health"
        };

        public static string GetComponentShortLabel(SourceHealthComponentType componentType) => componentType switch
        {
            SourceHealthComponentType.Catalog => "Catalog",
            SourceHealthComponentType.Live => "Live",
            SourceHealthComponentType.Vod => "VOD",
            SourceHealthComponentType.Epg => "EPG",
            SourceHealthComponentType.Logos => "Logos",
            SourceHealthComponentType.Freshness => "Fresh",
            _ => "Health"
        };

        public static string GetComponentStateLabel(SourceHealthComponentState state) => state switch
        {
            SourceHealthComponentState.Healthy => "Healthy",
            SourceHealthComponentState.Weak => "Weak",
            SourceHealthComponentState.Incomplete => "Incomplete",
            SourceHealthComponentState.Outdated => "Outdated",
            SourceHealthComponentState.Problematic => "Problematic",
            SourceHealthComponentState.NotApplicable => "Not applicable",
            _ => "Not synced"
        };

        public static string GetHealthStateLabel(SourceHealthState state) => state switch
        {
            SourceHealthState.Healthy => "Healthy",
            SourceHealthState.Good => "Good",
            SourceHealthState.Weak => "Weak",
            SourceHealthState.Incomplete => "Incomplete",
            SourceHealthState.Outdated => "Outdated",
            SourceHealthState.Problematic => "Problematic",
            SourceHealthState.Unknown => "Unknown",
            _ => "Not synced"
        };

        public static string GetComponentBadgeText(SourceHealthComponentState state) => state switch
        {
            SourceHealthComponentState.Healthy => "OK",
            SourceHealthComponentState.Weak => "WEAK",
            SourceHealthComponentState.Incomplete => "PARTIAL",
            SourceHealthComponentState.Outdated => "STALE",
            SourceHealthComponentState.Problematic => "ISSUE",
            SourceHealthComponentState.NotApplicable => "N/A",
            _ => "PENDING"
        };
    }
}

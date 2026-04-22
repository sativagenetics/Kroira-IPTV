#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public enum SourceActivityTone
    {
        Neutral = 0,
        Healthy = 1,
        Warning = 2,
        Failed = 3,
        Info = 4,
        Syncing = 5
    }

    public sealed class SourceActivitySnapshot
    {
        public int SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public SourceType SourceType { get; set; }
        public string HeadlineText { get; set; } = "No source activity yet";
        public string TrendText { get; set; } = string.Empty;
        public string CurrentStateText { get; set; } = string.Empty;
        public string LatestAttemptText { get; set; } = "No attempts recorded";
        public string LastSuccessText { get; set; } = "No successful source sync recorded";
        public string QuietStateText { get; set; } = "Activity will appear after syncs, guide refreshes, or playback attempts are recorded.";
        public string SafeReportText { get; set; } = string.Empty;
        public IReadOnlyList<SourceActivityMetric> Metrics { get; set; } = Array.Empty<SourceActivityMetric>();
        public IReadOnlyList<SourceActivityTimelineItem> Timeline { get; set; } = Array.Empty<SourceActivityTimelineItem>();
    }

    public sealed class SourceActivityMetric
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public SourceActivityTone Tone { get; set; } = SourceActivityTone.Neutral;
    }

    public sealed class SourceActivityTimelineItem
    {
        public DateTime TimestampUtc { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string BadgeText { get; set; } = string.Empty;
        public SourceActivityTone Tone { get; set; } = SourceActivityTone.Neutral;
    }
}

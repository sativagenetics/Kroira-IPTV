#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kroira.App.Controls;
using Kroira.App.Models;

namespace Kroira.App.ViewModels
{
    public partial class SourceItemViewModel : ObservableObject
    {
        public string ActivityHeadlineText { get; set; } = string.Empty;
        public string ActivityTrendText { get; set; } = string.Empty;
        public string ActivityCurrentStateText { get; set; } = string.Empty;
        public string ActivityLatestAttemptText { get; set; } = string.Empty;
        public string ActivityLastSuccessText { get; set; } = string.Empty;
        public string ActivityQuietStateText { get; set; } = string.Empty;
        public string ActivitySafeReportText { get; set; } = string.Empty;
        public IReadOnlyList<SourceActivityMetricItemViewModel> ActivityMetrics { get; set; } = Array.Empty<SourceActivityMetricItemViewModel>();
        public IReadOnlyList<SourceActivityTimelineItemViewModel> ActivityTimeline { get; set; } = Array.Empty<SourceActivityTimelineItemViewModel>();

        public Microsoft.UI.Xaml.Visibility ActivityHeadlineVisibility => string.IsNullOrWhiteSpace(ActivityHeadlineText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility ActivityTrendVisibility => string.IsNullOrWhiteSpace(ActivityTrendText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility ActivityCurrentStateVisibility => string.IsNullOrWhiteSpace(ActivityCurrentStateText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility ActivityLatestAttemptVisibility => string.IsNullOrWhiteSpace(ActivityLatestAttemptText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility ActivityLastSuccessVisibility => string.IsNullOrWhiteSpace(ActivityLastSuccessText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility ActivityMetricsVisibility => ActivityMetrics.Count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility ActivityTimelineVisibility => ActivityTimeline.Count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility ActivityQuietStateVisibility => string.IsNullOrWhiteSpace(ActivityQuietStateText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public bool HasSafeActivityReport => !string.IsNullOrWhiteSpace(ActivitySafeReportText);
    }

    public sealed class SourceActivityMetricItemViewModel
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public StatusPillKind PillKind { get; set; } = StatusPillKind.Neutral;

        public Microsoft.UI.Xaml.Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
    }

    public sealed class SourceActivityTimelineItemViewModel
    {
        public string TimestampText { get; set; } = string.Empty;
        public string CategoryText { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string PillLabel { get; set; } = string.Empty;
        public StatusPillKind PillKind { get; set; } = StatusPillKind.Neutral;

        public Microsoft.UI.Xaml.Visibility SubtitleVisibility => string.IsNullOrWhiteSpace(Subtitle)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
    }

    public partial class SourceListViewModel
    {
        public string GetSafeActivityReport(int sourceId)
        {
            return _allSources.FirstOrDefault(item => item.Id == sourceId)?.ActivitySafeReportText ?? string.Empty;
        }

        private static IReadOnlyList<SourceActivityMetricItemViewModel> BuildActivityMetrics(SourceActivitySnapshot snapshot)
        {
            return snapshot.Metrics
                .Select(metric => new SourceActivityMetricItemViewModel
                {
                    Label = metric.Label,
                    Value = metric.Value,
                    Detail = metric.Detail,
                    PillKind = MapActivityTone(metric.Tone)
                })
                .ToList();
        }

        private static IReadOnlyList<SourceActivityTimelineItemViewModel> BuildActivityTimeline(SourceActivitySnapshot snapshot)
        {
            return snapshot.Timeline
                .Select(item => new SourceActivityTimelineItemViewModel
                {
                    TimestampText = item.TimestampUtc > DateTime.MinValue
                        ? item.TimestampUtc.ToLocalTime().ToString("MMM d, HH:mm")
                        : string.Empty,
                    CategoryText = item.Category,
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    Detail = item.Detail,
                    PillLabel = item.BadgeText,
                    PillKind = MapActivityTone(item.Tone)
                })
                .ToList();
        }

        private static StatusPillKind MapActivityTone(SourceActivityTone tone)
        {
            return tone switch
            {
                SourceActivityTone.Healthy => StatusPillKind.Healthy,
                SourceActivityTone.Warning => StatusPillKind.Warning,
                SourceActivityTone.Failed => StatusPillKind.Failed,
                SourceActivityTone.Info => StatusPillKind.Info,
                SourceActivityTone.Syncing => StatusPillKind.Syncing,
                _ => StatusPillKind.Neutral
            };
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Kroira.App.Controls;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.ViewModels
{
    public partial class SourceItemViewModel : ObservableObject
    {
        public string RepairHeadlineText { get; set; } = string.Empty;
        public string RepairSummaryText { get; set; } = string.Empty;
        public string RepairStatusText { get; set; } = string.Empty;
        public string RepairStatusBadgeText { get; set; } = "Stable";
        public StatusPillKind RepairStatusKind { get; set; } = StatusPillKind.Neutral;
        public string RepairCapabilitySummaryText { get; set; } = string.Empty;
        public string RepairSafeReportText { get; set; } = string.Empty;
        public string RepairLatestResultHeadlineText { get; set; } = string.Empty;
        public string RepairLatestResultDetailText { get; set; } = string.Empty;
        public string RepairLatestResultChangeText { get; set; } = string.Empty;
        public string RepairLatestResultSafeReportText { get; set; } = string.Empty;
        public StatusPillKind RepairLatestResultKind { get; set; } = StatusPillKind.Neutral;
        public IReadOnlyList<SourceRepairCapabilityItemViewModel> RepairCapabilities { get; set; } = Array.Empty<SourceRepairCapabilityItemViewModel>();
        public IReadOnlyList<SourceRepairIssueItemViewModel> RepairIssues { get; set; } = Array.Empty<SourceRepairIssueItemViewModel>();
        public IReadOnlyList<SourceRepairActionItemViewModel> RepairActions { get; set; } = Array.Empty<SourceRepairActionItemViewModel>();

        public Microsoft.UI.Xaml.Visibility RepairHeadlineVisibility => string.IsNullOrWhiteSpace(RepairHeadlineText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility RepairSummaryVisibility => string.IsNullOrWhiteSpace(RepairSummaryText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility RepairStatusVisibility => string.IsNullOrWhiteSpace(RepairStatusText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility RepairCapabilitySummaryVisibility => string.IsNullOrWhiteSpace(RepairCapabilitySummaryText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility RepairCapabilitiesVisibility => RepairCapabilities.Count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility RepairIssuesVisibility => RepairIssues.Count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility RepairActionsVisibility => RepairActions.Count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility RepairLatestResultVisibility => string.IsNullOrWhiteSpace(RepairLatestResultHeadlineText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility RepairLatestResultDetailVisibility => string.IsNullOrWhiteSpace(RepairLatestResultDetailText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility RepairLatestResultChangeVisibility => string.IsNullOrWhiteSpace(RepairLatestResultChangeText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public bool HasSafeRepairReport => !string.IsNullOrWhiteSpace(RepairSafeReportText) || !string.IsNullOrWhiteSpace(RepairLatestResultSafeReportText);
    }

    public sealed class SourceRepairCapabilityItemViewModel
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public StatusPillKind Kind { get; set; } = StatusPillKind.Neutral;

        public Microsoft.UI.Xaml.Visibility DetailVisibility => string.IsNullOrWhiteSpace(Detail)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
    }

    public sealed class SourceRepairIssueItemViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public StatusPillKind Kind { get; set; } = StatusPillKind.Neutral;
    }

    public sealed class SourceRepairActionItemViewModel
    {
        public int SourceId { get; set; }
        public SourceRepairActionType ActionType { get; set; }
        public SourceRepairActionKind Kind { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string ButtonText { get; set; } = string.Empty;
        public StatusPillKind ToneKind { get; set; } = StatusPillKind.Neutral;
        public bool IsPrimary { get; set; }

        public Microsoft.UI.Xaml.Visibility PrimaryButtonVisibility => IsPrimary
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility SecondaryButtonVisibility => !IsPrimary
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public partial class SourceListViewModel
    {
        private readonly Dictionary<int, SourceRepairExecutionResult> _repairResults = new();

        public string GetSafeRepairReport(int sourceId)
        {
            var source = _allSources.FirstOrDefault(item => item.Id == sourceId);
            if (source == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(source.RepairSafeReportText))
            {
                builder.Append(source.RepairSafeReportText.Trim());
            }

            if (!string.IsNullOrWhiteSpace(source.RepairLatestResultSafeReportText))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.Append(source.RepairLatestResultSafeReportText.Trim());
            }

            return builder.ToString();
        }

        public async Task<SourceRepairExecutionResult?> ApplyRepairActionAsync(int sourceId, SourceRepairActionType actionType)
        {
            using var scope = _serviceProvider.CreateScope();
            var guidanceService = scope.ServiceProvider.GetRequiredService<ISourceGuidanceService>();
            var result = await guidanceService.ApplyRepairActionAsync(sourceId, actionType);
            _repairResults[sourceId] = result;
            await LoadSourcesAsync();
            return result;
        }

        private static IReadOnlyList<SourceRepairCapabilityItemViewModel> BuildRepairCapabilities(SourceRepairSnapshot snapshot)
        {
            return snapshot.Capabilities
                .Select(capability => new SourceRepairCapabilityItemViewModel
                {
                    Label = capability.Label,
                    Value = capability.Value,
                    Detail = capability.Detail,
                    Kind = MapActivityTone(capability.Tone)
                })
                .ToList();
        }

        private static IReadOnlyList<SourceRepairIssueItemViewModel> BuildRepairIssues(SourceRepairSnapshot snapshot)
        {
            return snapshot.Issues
                .Select(issue => new SourceRepairIssueItemViewModel
                {
                    Title = issue.Title,
                    Detail = issue.Detail,
                    Kind = MapActivityTone(issue.Tone)
                })
                .ToList();
        }

        private static IReadOnlyList<SourceRepairActionItemViewModel> BuildRepairActions(SourceRepairSnapshot snapshot)
        {
            return snapshot.Actions
                .Select(action => new SourceRepairActionItemViewModel
                {
                    SourceId = snapshot.SourceId,
                    ActionType = action.ActionType,
                    Kind = action.Kind,
                    Title = action.Title,
                    Summary = action.Summary,
                    ButtonText = action.ButtonText,
                    ToneKind = MapActivityTone(action.Tone),
                    IsPrimary = action.IsPrimary
                })
                .ToList();
        }

        private static StatusPillKind MapRepairStatusKind(SourceRepairSnapshot snapshot)
        {
            if (snapshot.Issues.Any(issue => issue.Tone == SourceActivityTone.Failed))
            {
                return StatusPillKind.Failed;
            }

            if (snapshot.Issues.Any(issue => issue.Tone == SourceActivityTone.Warning))
            {
                return StatusPillKind.Warning;
            }

            return snapshot.IsStable
                ? StatusPillKind.Healthy
                : StatusPillKind.Neutral;
        }

        private static string BuildRepairStatusBadgeText(SourceRepairSnapshot snapshot)
        {
            return MapRepairStatusKind(snapshot) switch
            {
                StatusPillKind.Failed => "Fix first",
                StatusPillKind.Warning => "Needs attention",
                StatusPillKind.Healthy => "Stable",
                _ => "Review"
            };
        }
    }
}

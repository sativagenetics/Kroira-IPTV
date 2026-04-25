#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public sealed class SourceSetupDraft
    {
        public string Name { get; set; } = string.Empty;
        public SourceType Type { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ManualEpgUrl { get; set; } = string.Empty;
        public string FallbackEpgUrls { get; set; } = string.Empty;
        public EpgActiveMode EpgMode { get; set; } = EpgActiveMode.Detected;
        public SourceProxyScope ProxyScope { get; set; } = SourceProxyScope.Disabled;
        public string ProxyUrl { get; set; } = string.Empty;
        public SourceCompanionScope CompanionScope { get; set; } = SourceCompanionScope.Disabled;
        public SourceCompanionRelayMode CompanionMode { get; set; } = SourceCompanionRelayMode.Buffered;
        public string CompanionUrl { get; set; } = string.Empty;
        public string StalkerMacAddress { get; set; } = string.Empty;
        public string StalkerDeviceId { get; set; } = string.Empty;
        public string StalkerSerialNumber { get; set; } = string.Empty;
        public string StalkerTimezone { get; set; } = string.Empty;
        public string StalkerLocale { get; set; } = string.Empty;
    }

    public sealed class SourceGuidanceCapability
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public SourceActivityTone Tone { get; set; } = SourceActivityTone.Neutral;
    }

    public sealed class SourceGuidanceIssue
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public SourceActivityTone Tone { get; set; } = SourceActivityTone.Neutral;
    }

    public sealed class SourceSetupValidationSnapshot
    {
        public SourceType RequestedType { get; set; }
        public SourceType? DetectedTypeHint { get; set; }
        public bool CanSave { get; set; }
        public string HeadlineText { get; set; } = string.Empty;
        public string SummaryText { get; set; } = string.Empty;
        public string ConnectionText { get; set; } = string.Empty;
        public string TypeHintText { get; set; } = string.Empty;
        public string CapabilitySummaryText { get; set; } = string.Empty;
        public string SafeReportText { get; set; } = string.Empty;
        public IReadOnlyList<SourceGuidanceCapability> Capabilities { get; set; } = Array.Empty<SourceGuidanceCapability>();
        public IReadOnlyList<SourceGuidanceIssue> Issues { get; set; } = Array.Empty<SourceGuidanceIssue>();
    }

    public enum SourceRepairActionType
    {
        RetestSource = 0,
        RetestGuide = 1,
        DisableCompanionRelay = 2,
        RepairRuntimeState = 3,
        RefreshPortalProfile = 4,
        ReviewGuideSettings = 5,
        RunStreamProbe = 6
    }

    public enum SourceRepairActionKind
    {
        Apply = 0,
        Review = 1
    }

    public sealed class SourceRepairAction
    {
        public SourceRepairActionType ActionType { get; set; }
        public SourceRepairActionKind Kind { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string ButtonText { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public SourceActivityTone Tone { get; set; } = SourceActivityTone.Neutral;
    }

    public sealed class SourceRepairSnapshot
    {
        public int SourceId { get; set; }
        public string HeadlineText { get; set; } = string.Empty;
        public string SummaryText { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string CapabilitySummaryText { get; set; } = string.Empty;
        public string SafeReportText { get; set; } = string.Empty;
        public bool IsStable { get; set; }
        public IReadOnlyList<SourceGuidanceCapability> Capabilities { get; set; } = Array.Empty<SourceGuidanceCapability>();
        public IReadOnlyList<SourceGuidanceIssue> Issues { get; set; } = Array.Empty<SourceGuidanceIssue>();
        public IReadOnlyList<SourceRepairAction> Actions { get; set; } = Array.Empty<SourceRepairAction>();
    }

    public sealed class SourceRepairExecutionResult
    {
        public int SourceId { get; set; }
        public SourceRepairActionType ActionType { get; set; }
        public bool Success { get; set; }
        public string HeadlineText { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
        public string ChangeText { get; set; } = string.Empty;
        public string SafeReportText { get; set; } = string.Empty;
    }
}

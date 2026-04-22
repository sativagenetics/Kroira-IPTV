#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public sealed class PlayableItemInspectionRuntimeState
    {
        public bool IsCurrentPlayback { get; set; }
        public string SessionState { get; set; } = string.Empty;
        public string SessionMessage { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public double FramesPerSecond { get; set; }
        public string VideoCodec { get; set; } = string.Empty;
        public string AudioCodec { get; set; } = string.Empty;
        public string ContainerFormat { get; set; } = string.Empty;
        public string PixelFormat { get; set; } = string.Empty;
        public bool IsHardwareDecodingActive { get; set; }
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
        public bool IsSeekable { get; set; }
    }

    public sealed class PlayableItemInspectionSnapshot
    {
        public bool IsCurrentPlayback { get; set; }
        public bool SupportsExternalLaunch { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string FailureText { get; set; } = string.Empty;
        public string SafetyText { get; set; } = "Sensitive values are redacted in this view and in copied diagnostics.";
        public string SafeReportText { get; set; } = string.Empty;
        public IReadOnlyList<PlayableItemInspectionSection> Sections { get; set; } = Array.Empty<PlayableItemInspectionSection>();
    }

    public sealed class PlayableItemInspectionSection
    {
        public string Title { get; set; } = string.Empty;
        public IReadOnlyList<PlayableItemInspectionField> Fields { get; set; } = Array.Empty<PlayableItemInspectionField>();
    }

    public sealed class PlayableItemInspectionField
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public sealed class ExternalPlayerLaunchResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ProviderSummary { get; set; } = string.Empty;
        public string RoutingSummary { get; set; } = string.Empty;
        public string ResolvedUrlText { get; set; } = string.Empty;
        public bool UsedApplicationPicker { get; set; }
    }
}

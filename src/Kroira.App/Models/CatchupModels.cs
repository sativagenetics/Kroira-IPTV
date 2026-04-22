using System;

namespace Kroira.App.Models
{
    public enum CatchupPlaybackMode
    {
        Live = 0,
        Catchup = 1
    }

    public enum CatchupRequestKind
    {
        None = 0,
        StartOver = 1,
        ReplayProgram = 2
    }

    public enum CatchupAvailabilityState
    {
        None = 0,
        Available = 1,
        Unsupported = 2,
        MissingWindow = 3,
        Expired = 4,
        Future = 5,
        MissingTemplate = 6
    }

    public enum CatchupResolutionStatus
    {
        None = 0,
        Resolved = 1,
        Unsupported = 2,
        ProgramMissing = 3,
        MissingWindow = 4,
        Expired = 5,
        MissingTemplate = 6,
        MissingCredential = 7,
        InvalidStream = 8,
        InvalidTemplate = 9,
        Failed = 10
    }

    public sealed class CatchupPlaybackRequest
    {
        public int ProfileId { get; set; }
        public int ChannelId { get; set; }
        public string LogicalContentKey { get; set; } = string.Empty;
        public int PreferredSourceProfileId { get; set; }
        public CatchupRequestKind RequestKind { get; set; }
        public string ProgramTitle { get; set; } = string.Empty;
        public DateTime? ProgramStartTimeUtc { get; set; }
        public DateTime? ProgramEndTimeUtc { get; set; }
        public DateTime? RequestedAtUtc { get; set; }
    }

    public sealed class CatchupPlaybackResolution
    {
        public CatchupResolutionStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public string UpstreamStreamUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string LiveStreamUrl { get; set; } = string.Empty;
        public string RoutingSummary { get; set; } = string.Empty;
        public int SourceProfileId { get; set; }
        public int ChannelId { get; set; }
        public string LogicalContentKey { get; set; } = string.Empty;
        public string ProgramTitle { get; set; } = string.Empty;
        public DateTime? ProgramStartTimeUtc { get; set; }
        public DateTime? ProgramEndTimeUtc { get; set; }
        public CatchupRequestKind RequestKind { get; set; }
        public DateTime? RequestedAtUtc { get; set; }

        public bool Success => Status == CatchupResolutionStatus.Resolved;
    }

    public sealed class CatchupProgramAvailability
    {
        public CatchupAvailabilityState State { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        public CatchupRequestKind RequestKind { get; set; }
        public bool CanPlay => State == CatchupAvailabilityState.Available && RequestKind != CatchupRequestKind.None;
    }

    public class CatchupPlaybackAttempt
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public int ChannelId { get; set; }
        public string LogicalContentKey { get; set; } = string.Empty;
        public CatchupRequestKind RequestKind { get; set; }
        public CatchupResolutionStatus Status { get; set; }
        public DateTime RequestedAtUtc { get; set; }
        public string ProgramTitle { get; set; } = string.Empty;
        public DateTime? ProgramStartTimeUtc { get; set; }
        public DateTime? ProgramEndTimeUtc { get; set; }
        public int WindowHours { get; set; }
        public string Message { get; set; } = string.Empty;
        public string RoutingSummary { get; set; } = string.Empty;
        public string ResolvedStreamUrl { get; set; } = string.Empty;
        public string ProviderMode { get; set; } = string.Empty;
        public string ProviderSource { get; set; } = string.Empty;
    }
}

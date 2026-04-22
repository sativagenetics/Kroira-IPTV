using System;
using Kroira.App.Services.Playback;

namespace Kroira.App.Models
{
    public class PlaybackLaunchContext
    {
        public int ProfileId { get; set; }
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public CatchupPlaybackMode PlaybackMode { get; set; } = CatchupPlaybackMode.Live;
        public string LogicalContentKey { get; set; } = string.Empty;
        public int PreferredSourceProfileId { get; set; }
        public string CatalogStreamUrl { get; set; } = string.Empty;
        public string UpstreamStreamUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string LiveStreamUrl { get; set; } = string.Empty;
        public SourceProxyScope ProxyScope { get; set; }
        public string ProxyUrl { get; set; } = string.Empty;
        public SourceCompanionScope CompanionScope { get; set; }
        public SourceCompanionRelayMode CompanionMode { get; set; } = SourceCompanionRelayMode.Buffered;
        public string CompanionUrl { get; set; } = string.Empty;
        public CompanionRelayStatus CompanionStatus { get; set; }
        public string CompanionStatusText { get; set; } = string.Empty;
        public string RoutingSummary { get; set; } = string.Empty;
        public string ProviderSummary { get; set; } = string.Empty;
        public string OperationalSummary { get; set; } = string.Empty;
        public int MirrorCandidateCount { get; set; }
        public CatchupRequestKind CatchupRequestKind { get; set; }
        public CatchupResolutionStatus CatchupResolutionStatus { get; set; }
        public string CatchupStatusText { get; set; } = string.Empty;
        public string CatchupProgramTitle { get; set; } = string.Empty;
        public DateTime? CatchupProgramStartTimeUtc { get; set; }
        public DateTime? CatchupProgramEndTimeUtc { get; set; }
        public DateTime? CatchupRequestedAtUtc { get; set; }
        public long StartPositionMs { get; set; }
        public long HostWindowHandle { get; set; }
        public bool OpenInPictureInPicture { get; set; }
        public bool StartPaused { get; set; }
        public double InitialVolume { get; set; } = 100;
        public bool IsMuted { get; set; }
        public PlaybackAspectMode InitialAspectMode { get; set; } = PlaybackAspectMode.Automatic;
        public double InitialPlaybackSpeed { get; set; } = 1.0;
        public double AudioDelaySeconds { get; set; }
        public double SubtitleDelaySeconds { get; set; }
        public double SubtitleScale { get; set; } = 1.0;
        public int SubtitlePosition { get; set; } = 100;
        public bool SubtitlesEnabled { get; set; } = true;
        public bool Deinterlace { get; set; }
        public bool RestoreAudioTrackSelection { get; set; }
        public string PreferredAudioTrackId { get; set; } = string.Empty;
        public bool RestoreSubtitleTrackSelection { get; set; }
        public string PreferredSubtitleTrackId { get; set; } = string.Empty;

        public PlaybackLaunchContext Clone()
        {
            return new PlaybackLaunchContext
            {
                ProfileId = ProfileId,
                ContentId = ContentId,
                ContentType = ContentType,
                PlaybackMode = PlaybackMode,
                LogicalContentKey = LogicalContentKey,
                PreferredSourceProfileId = PreferredSourceProfileId,
                CatalogStreamUrl = CatalogStreamUrl,
                UpstreamStreamUrl = UpstreamStreamUrl,
                StreamUrl = StreamUrl,
                LiveStreamUrl = LiveStreamUrl,
                ProxyScope = ProxyScope,
                ProxyUrl = ProxyUrl,
                CompanionScope = CompanionScope,
                CompanionMode = CompanionMode,
                CompanionUrl = CompanionUrl,
                CompanionStatus = CompanionStatus,
                CompanionStatusText = CompanionStatusText,
                RoutingSummary = RoutingSummary,
                ProviderSummary = ProviderSummary,
                OperationalSummary = OperationalSummary,
                MirrorCandidateCount = MirrorCandidateCount,
                CatchupRequestKind = CatchupRequestKind,
                CatchupResolutionStatus = CatchupResolutionStatus,
                CatchupStatusText = CatchupStatusText,
                CatchupProgramTitle = CatchupProgramTitle,
                CatchupProgramStartTimeUtc = CatchupProgramStartTimeUtc,
                CatchupProgramEndTimeUtc = CatchupProgramEndTimeUtc,
                CatchupRequestedAtUtc = CatchupRequestedAtUtc,
                StartPositionMs = StartPositionMs,
                HostWindowHandle = HostWindowHandle,
                OpenInPictureInPicture = OpenInPictureInPicture,
                StartPaused = StartPaused,
                InitialVolume = InitialVolume,
                IsMuted = IsMuted,
                InitialAspectMode = InitialAspectMode,
                InitialPlaybackSpeed = InitialPlaybackSpeed,
                AudioDelaySeconds = AudioDelaySeconds,
                SubtitleDelaySeconds = SubtitleDelaySeconds,
                SubtitleScale = SubtitleScale,
                SubtitlePosition = SubtitlePosition,
                SubtitlesEnabled = SubtitlesEnabled,
                Deinterlace = Deinterlace,
                RestoreAudioTrackSelection = RestoreAudioTrackSelection,
                PreferredAudioTrackId = PreferredAudioTrackId,
                RestoreSubtitleTrackSelection = RestoreSubtitleTrackSelection,
                PreferredSubtitleTrackId = PreferredSubtitleTrackId
            };
        }
    }
}

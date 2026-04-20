using Kroira.App.Services.Playback;

namespace Kroira.App.Models
{
    public class PlaybackLaunchContext
    {
        public int ProfileId { get; set; }
        public int ContentId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public string StreamUrl { get; set; } = string.Empty;
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
    }
}

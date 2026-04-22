#nullable enable
namespace Kroira.App.Models
{
    public enum PlaybackRemoteCommand
    {
        None = 0,
        TogglePlayPause,
        SeekBackward10,
        SeekBackward30,
        SeekForward10,
        SeekForward30,
        VolumeUp,
        VolumeDown,
        PreviousChannel,
        NextChannel,
        LastChannel,
        ToggleFullscreen,
        ToggleMute,
        ToggleSubtitles,
        OpenTrackSelection,
        ToggleInfoPanel,
        ToggleGuidePanel,
        RestartOrStartOver,
        GoLive,
        StopPlayback,
        CloseContext
    }

    public sealed class PlaybackRemoteContext
    {
        public bool IsTextInputFocused { get; set; }
        public bool IsMenuOpen { get; set; }
        public bool ReserveFocusedControlKeys { get; set; }
        public bool IsPictureInPicture { get; set; }
        public bool IsLivePlayback { get; set; }
        public bool IsChannelPlayback { get; set; }
        public bool CanSeek { get; set; }
        public bool HasLastChannel { get; set; }
        public bool CanRestartOrStartOver { get; set; }
        public bool CanGoLive { get; set; }
    }
}

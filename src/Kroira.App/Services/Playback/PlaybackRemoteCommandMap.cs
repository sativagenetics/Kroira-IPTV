#nullable enable
using Kroira.App.Models;
using Windows.System;

namespace Kroira.App.Services.Playback
{
    public static class PlaybackRemoteCommandMap
    {
        public static PlaybackRemoteCommand Resolve(VirtualKey key, bool shiftDown, PlaybackRemoteContext context)
        {
            if (context.IsTextInputFocused && key != VirtualKey.Escape)
            {
                return PlaybackRemoteCommand.None;
            }

            if (context.IsMenuOpen && key != VirtualKey.Escape)
            {
                return PlaybackRemoteCommand.None;
            }

            var reserveFocusedControlKeys = context.ReserveFocusedControlKeys;
            return key switch
            {
                VirtualKey.Space when !reserveFocusedControlKeys => PlaybackRemoteCommand.TogglePlayPause,
                VirtualKey.Enter when !reserveFocusedControlKeys => PlaybackRemoteCommand.TogglePlayPause,
                VirtualKey.Left when context.CanSeek && !reserveFocusedControlKeys => shiftDown
                    ? PlaybackRemoteCommand.SeekBackward30
                    : PlaybackRemoteCommand.SeekBackward10,
                VirtualKey.Right when context.CanSeek && !reserveFocusedControlKeys => shiftDown
                    ? PlaybackRemoteCommand.SeekForward30
                    : PlaybackRemoteCommand.SeekForward10,
                VirtualKey.Up when !reserveFocusedControlKeys => PlaybackRemoteCommand.VolumeUp,
                VirtualKey.Down when !reserveFocusedControlKeys => PlaybackRemoteCommand.VolumeDown,
                VirtualKey.PageUp when context.IsLivePlayback && !reserveFocusedControlKeys => PlaybackRemoteCommand.PreviousChannel,
                VirtualKey.PageDown when context.IsLivePlayback && !reserveFocusedControlKeys => PlaybackRemoteCommand.NextChannel,
                VirtualKey.Back when context.IsLivePlayback && context.HasLastChannel && !reserveFocusedControlKeys => PlaybackRemoteCommand.LastChannel,
                VirtualKey.F when !shiftDown && !context.IsPictureInPicture => PlaybackRemoteCommand.ToggleFullscreen,
                VirtualKey.M => PlaybackRemoteCommand.ToggleMute,
                VirtualKey.S => PlaybackRemoteCommand.ToggleSubtitles,
                VirtualKey.A => PlaybackRemoteCommand.OpenTrackSelection,
                VirtualKey.I => PlaybackRemoteCommand.ToggleInfoPanel,
                VirtualKey.G when context.IsChannelPlayback => PlaybackRemoteCommand.ToggleGuidePanel,
                VirtualKey.Home when context.CanRestartOrStartOver && !reserveFocusedControlKeys => PlaybackRemoteCommand.RestartOrStartOver,
                VirtualKey.End when context.CanGoLive && !reserveFocusedControlKeys => PlaybackRemoteCommand.GoLive,
                VirtualKey.Escape => PlaybackRemoteCommand.CloseContext,
                _ => PlaybackRemoteCommand.None
            };
        }
    }
}

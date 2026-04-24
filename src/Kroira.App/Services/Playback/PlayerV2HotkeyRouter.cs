#nullable enable
namespace Kroira.App.Services.Playback
{
    public enum PlayerV2Key
    {
        Other = 0,
        Space,
        K,
        F,
        M,
        I,
        Escape,
        Left,
        Right,
        Up,
        Down
    }

    public enum PlayerV2HotkeyCommand
    {
        None = 0,
        TogglePlayPause,
        ToggleFullscreen,
        ToggleMute,
        ToggleInfoPanel,
        CloseMenuOrPanel,
        ExitFullscreen,
        NavigateBack,
        SeekBackward,
        SeekForward,
        VolumeUp,
        VolumeDown
    }

    public sealed class PlayerV2HotkeyContext
    {
        public bool IsEditableControlFocused { get; init; }

        public bool IsMenuOpen { get; init; }

        public bool IsPanelOpen { get; init; }

        public bool IsFullscreen { get; init; }

        public bool IsSeekableVod { get; init; }
    }

    public static class PlayerV2HotkeyRouter
    {
        public static PlayerV2HotkeyCommand Resolve(PlayerV2Key key, PlayerV2HotkeyContext? context = null)
        {
            context ??= new PlayerV2HotkeyContext();

            if (context.IsEditableControlFocused)
            {
                return PlayerV2HotkeyCommand.None;
            }

            if (key == PlayerV2Key.Escape)
            {
                return ResolveEscape(context);
            }

            if (context.IsMenuOpen)
            {
                return PlayerV2HotkeyCommand.None;
            }

            return key switch
            {
                PlayerV2Key.Space or PlayerV2Key.K => PlayerV2HotkeyCommand.TogglePlayPause,
                PlayerV2Key.F => PlayerV2HotkeyCommand.ToggleFullscreen,
                PlayerV2Key.M => PlayerV2HotkeyCommand.ToggleMute,
                PlayerV2Key.I => PlayerV2HotkeyCommand.ToggleInfoPanel,
                PlayerV2Key.Left when context.IsSeekableVod => PlayerV2HotkeyCommand.SeekBackward,
                PlayerV2Key.Right when context.IsSeekableVod => PlayerV2HotkeyCommand.SeekForward,
                PlayerV2Key.Up => PlayerV2HotkeyCommand.VolumeUp,
                PlayerV2Key.Down => PlayerV2HotkeyCommand.VolumeDown,
                _ => PlayerV2HotkeyCommand.None
            };
        }

        private static PlayerV2HotkeyCommand ResolveEscape(PlayerV2HotkeyContext context)
        {
            if (context.IsMenuOpen || context.IsPanelOpen)
            {
                return PlayerV2HotkeyCommand.CloseMenuOrPanel;
            }

            return context.IsFullscreen
                ? PlayerV2HotkeyCommand.ExitFullscreen
                : PlayerV2HotkeyCommand.NavigateBack;
        }
    }
}

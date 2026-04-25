#nullable enable
namespace Kroira.App.Services.Playback
{
    public sealed class PlayerV2OverlayStateController
    {
        public PlaybackSessionState PlaybackState { get; private set; } = PlaybackSessionState.Idle;

        public bool IsOverlayVisible { get; private set; } = true;

        public bool IsMenuOpen { get; private set; }

        public bool IsFullscreen { get; private set; }

        public bool CanAutoHide => PlaybackState == PlaybackSessionState.Playing && !IsMenuOpen;

        public void SetPlaybackState(PlaybackSessionState state)
        {
            PlaybackState = state;

            if (ShouldForceOverlayVisible(state) || IsMenuOpen)
            {
                ShowOverlay();
                return;
            }

            if (state == PlaybackSessionState.Playing)
            {
                ShowOverlay();
            }
        }

        public void SetMenuOpen(bool isOpen)
        {
            IsMenuOpen = isOpen;
            ShowOverlay();
        }

        public void NotifyKeyboardInteraction()
        {
            ShowOverlay();
        }

        public void SetFullscreen(bool isFullscreen)
        {
            IsFullscreen = isFullscreen;
            ShowOverlay();
        }

        public bool TryAutoHide()
        {
            if (!CanAutoHide)
            {
                ShowOverlay();
                return false;
            }

            IsOverlayVisible = false;
            return true;
        }

        public void ShowOverlay()
        {
            IsOverlayVisible = true;
        }

        private static bool ShouldForceOverlayVisible(PlaybackSessionState state)
        {
            return state is PlaybackSessionState.Idle
                or PlaybackSessionState.Opening
                or PlaybackSessionState.Buffering
                or PlaybackSessionState.Paused
                or PlaybackSessionState.Error
                or PlaybackSessionState.Ended;
        }
    }
}

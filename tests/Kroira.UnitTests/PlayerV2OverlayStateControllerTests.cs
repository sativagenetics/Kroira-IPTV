using Kroira.App.Services.Playback;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class PlayerV2OverlayStateControllerTests
{
    [TestMethod]
    public void PausedOpeningBufferingAndError_ForceOverlayVisible()
    {
        foreach (var state in new[]
                 {
                     PlaybackSessionState.Paused,
                     PlaybackSessionState.Opening,
                     PlaybackSessionState.Buffering,
                     PlaybackSessionState.Error
                 })
        {
            var overlay = HiddenPlayingOverlay();

            overlay.SetPlaybackState(state);

            Assert.IsTrue(overlay.IsOverlayVisible, state.ToString());
            Assert.IsFalse(overlay.CanAutoHide, state.ToString());
        }
    }

    [TestMethod]
    public void Playing_AllowsAutoHideAndHidesWhenTimerElapses()
    {
        var overlay = new PlayerV2OverlayStateController();

        overlay.SetPlaybackState(PlaybackSessionState.Playing);
        var hidden = overlay.TryAutoHide();

        Assert.IsTrue(hidden);
        Assert.IsFalse(overlay.IsOverlayVisible);
        Assert.IsTrue(overlay.CanAutoHide);
    }

    [TestMethod]
    public void MenuOpen_KeepsOverlayVisibleAndBlocksAutoHide()
    {
        var overlay = new PlayerV2OverlayStateController();
        overlay.SetPlaybackState(PlaybackSessionState.Playing);
        Assert.IsTrue(overlay.TryAutoHide());

        overlay.SetMenuOpen(true);
        var hidden = overlay.TryAutoHide();

        Assert.IsFalse(hidden);
        Assert.IsTrue(overlay.IsOverlayVisible);
        Assert.IsFalse(overlay.CanAutoHide);
    }

    [TestMethod]
    public void KeyboardInteraction_ShowsOverlayWhilePlaying()
    {
        var overlay = HiddenPlayingOverlay();

        overlay.NotifyKeyboardInteraction();

        Assert.IsTrue(overlay.IsOverlayVisible);
        Assert.IsTrue(overlay.CanAutoHide);
    }

    [TestMethod]
    public void FullscreenEnterAndExit_UpdateStateAndShowOverlay()
    {
        var overlay = HiddenPlayingOverlay();

        overlay.SetFullscreen(true);
        Assert.IsTrue(overlay.IsFullscreen);
        Assert.IsTrue(overlay.IsOverlayVisible);

        overlay.TryAutoHide();
        overlay.SetFullscreen(false);
        Assert.IsFalse(overlay.IsFullscreen);
        Assert.IsTrue(overlay.IsOverlayVisible);
    }

    private static PlayerV2OverlayStateController HiddenPlayingOverlay()
    {
        var overlay = new PlayerV2OverlayStateController();
        overlay.SetPlaybackState(PlaybackSessionState.Playing);
        overlay.TryAutoHide();
        return overlay;
    }
}

using Kroira.App.Services.Playback;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class PlayerV2HotkeyRouterTests
{
    [TestMethod]
    public void SpaceAndK_TogglePlayPause()
    {
        Assert.AreEqual(
            PlayerV2HotkeyCommand.TogglePlayPause,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Space));
        Assert.AreEqual(
            PlayerV2HotkeyCommand.TogglePlayPause,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.K));
    }

    [TestMethod]
    public void F_M_AndI_MapToPlayerCommands()
    {
        Assert.AreEqual(PlayerV2HotkeyCommand.ToggleFullscreen, PlayerV2HotkeyRouter.Resolve(PlayerV2Key.F));
        Assert.AreEqual(PlayerV2HotkeyCommand.ToggleMute, PlayerV2HotkeyRouter.Resolve(PlayerV2Key.M));
        Assert.AreEqual(PlayerV2HotkeyCommand.ToggleInfoPanel, PlayerV2HotkeyRouter.Resolve(PlayerV2Key.I));
    }

    [TestMethod]
    public void Escape_ClosesMenuOrPanelBeforeFullscreenOrBack()
    {
        Assert.AreEqual(
            PlayerV2HotkeyCommand.CloseMenuOrPanel,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Escape, new PlayerV2HotkeyContext
            {
                IsMenuOpen = true,
                IsFullscreen = true
            }));

        Assert.AreEqual(
            PlayerV2HotkeyCommand.CloseMenuOrPanel,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Escape, new PlayerV2HotkeyContext
            {
                IsPanelOpen = true,
                IsFullscreen = true
            }));
    }

    [TestMethod]
    public void Escape_ExitsFullscreenBeforeNavigatingBack()
    {
        Assert.AreEqual(
            PlayerV2HotkeyCommand.ExitFullscreen,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Escape, new PlayerV2HotkeyContext
            {
                IsFullscreen = true
            }));

        Assert.AreEqual(
            PlayerV2HotkeyCommand.NavigateBack,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Escape));
    }

    [TestMethod]
    public void LeftAndRight_SeekOnlyForSeekableVod()
    {
        Assert.AreEqual(
            PlayerV2HotkeyCommand.None,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Left, new PlayerV2HotkeyContext
            {
                IsSeekableVod = false
            }));
        Assert.AreEqual(
            PlayerV2HotkeyCommand.None,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Right, new PlayerV2HotkeyContext
            {
                IsSeekableVod = false
            }));
        Assert.AreEqual(
            PlayerV2HotkeyCommand.SeekBackward,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Left, new PlayerV2HotkeyContext
            {
                IsSeekableVod = true
            }));
        Assert.AreEqual(
            PlayerV2HotkeyCommand.SeekForward,
            PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Right, new PlayerV2HotkeyContext
            {
                IsSeekableVod = true
            }));
    }

    [TestMethod]
    public void UpAndDown_AdjustVolume()
    {
        Assert.AreEqual(PlayerV2HotkeyCommand.VolumeUp, PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Up));
        Assert.AreEqual(PlayerV2HotkeyCommand.VolumeDown, PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Down));
    }

    [TestMethod]
    public void EditableControls_DoNotYieldPlayerHotkeys()
    {
        var context = new PlayerV2HotkeyContext
        {
            IsEditableControlFocused = true,
            IsMenuOpen = true,
            IsPanelOpen = true,
            IsFullscreen = true,
            IsSeekableVod = true
        };

        foreach (var key in new[]
                 {
                     PlayerV2Key.Space,
                     PlayerV2Key.K,
                     PlayerV2Key.F,
                     PlayerV2Key.M,
                     PlayerV2Key.I,
                     PlayerV2Key.Escape,
                     PlayerV2Key.Left,
                     PlayerV2Key.Right,
                     PlayerV2Key.Up,
                     PlayerV2Key.Down
                 })
        {
            Assert.AreEqual(PlayerV2HotkeyCommand.None, PlayerV2HotkeyRouter.Resolve(key, context), key.ToString());
        }
    }

    [TestMethod]
    public void OpenMenu_SuppressesNonEscapeHotkeys()
    {
        var context = new PlayerV2HotkeyContext
        {
            IsMenuOpen = true,
            IsSeekableVod = true
        };

        Assert.AreEqual(PlayerV2HotkeyCommand.None, PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Space, context));
        Assert.AreEqual(PlayerV2HotkeyCommand.CloseMenuOrPanel, PlayerV2HotkeyRouter.Resolve(PlayerV2Key.Escape, context));
    }
}

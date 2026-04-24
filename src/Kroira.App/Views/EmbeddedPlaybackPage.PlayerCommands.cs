#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Kroira.App.Views
{
    public sealed partial class EmbeddedPlaybackPage
    {
        private bool _isFavorite;
        private static readonly Brush ToolsButtonBackgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ToolsButtonBorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ToolsButtonForegroundBrush = new SolidColorBrush(Colors.White);
        private static readonly Brush ToolsButtonSelectedBackgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x4B, 0x7C, 0xFF));
        private static readonly Brush ToolsButtonSelectedBorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x66, 0xA9, 0xC0, 0xFF));
        private static readonly Brush ToolsButtonSelectedForegroundBrush = new SolidColorBrush(Colors.White);

        private void BuildSpeedFlyout()
        {
            _speedItems.Clear();
            SpeedFlyout.Items.Clear();
            foreach (var speed in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 })
            {
                var item = new ToggleMenuFlyoutItem
                {
                    Text = $"{speed:0.##}x",
                    Tag = speed
                };
                item.Click += SpeedItem_Click;
                SpeedFlyout.Items.Add(item);
                _speedItems.Add((speed, item));
            }
        }

        private void BuildToolsFlyout()
        {
            var isLive = IsLivePlayback();
            var isChannel = IsChannelPlayback();
            var canSeek = IsTimelineSeekAllowed();
            var hasEpisodeNavigation = _allEpisodeSwitchItems.Count > 1;
            var canUsePictureInPicture = CanUseFeature(EntitlementFeatureKeys.PlaybackPictureInPicture);
            ToolsGuideButton.Visibility = isChannel ? Visibility.Visible : Visibility.Collapsed;
            ToolsChannelsButton.Visibility = isChannel ? Visibility.Visible : Visibility.Collapsed;
            ToolsEpisodesButton.Visibility = hasEpisodeNavigation ? Visibility.Visible : Visibility.Collapsed;
            ToolsJumpLiveButton.Visibility = IsCatchupPlayback() || (isLive && canSeek) ? Visibility.Visible : Visibility.Collapsed;
            ToolsRestartButton.Visibility = !isLive && canSeek ? Visibility.Visible : Visibility.Collapsed;
            ToolsPictureInPictureButton.Visibility = canUsePictureInPicture ? Visibility.Visible : Visibility.Collapsed;
            ToolsPictureInPictureButton.Content = IsPictureInPictureMode() ? "Exit PiP" : "PiP";
            ToolsAlwaysOnTopButton.Visibility = IsPictureInPictureMode() ? Visibility.Collapsed : Visibility.Visible;
            ToolsPanelSummaryText.Text = IsCatchupPlayback()
                ? "Catchup replay stays on the live-channel path so you can jump back to live instantly."
                : isLive
                    ? "Live tools stay interactive while channel, subtitle, and display controls remain in frame."
                    : "Compact playback tuning for display, subtitles, recovery, and session tools.";

            RefreshToolToggleStates();
            UpdateToolsPanelVisibility();
        }

        private void UpdateToolsPanelVisibility()
        {
            ApplyToolButtonSelection(ToolsButton, _toolsPanelOpen);
        }

        private bool CloseToolsPanel()
        {
            if (!_toolsPanelOpen)
            {
                return false;
            }

            SetToolsPanelOpen(false, "close");
            return true;
        }

        private void SetToolsPanelOpen(bool isOpen, string reason)
        {
            if (_teardownStarted)
            {
                return;
            }

            if (_toolsPanelOpen == isOpen)
            {
                if (isOpen)
                {
                    BuildToolsFlyout();
                    ShowControls(persist: true, cause: reason);
                }
                else
                {
                    RefreshToolToggleStates();
                }
                return;
            }

            _toolsPanelOpen = isOpen;
            UpdateToolsPanelVisibility();
            LogPlaybackState($"TOOLS: panel open={BoolToLog(isOpen)} reason={reason}");

            if (isOpen)
            {
                BuildToolsFlyout();
                UpdateToolsPanelVisibility();
                SuppressSurfaceClicks();
                SetMenuSurfaceInputShield(true, "tools_open");
                ShowControls(persist: true, cause: "tools_open");
                _toolsMenuFlyout?.Hide();
                _toolsMenuFlyout = CreateToolsMenuFlyout();
                _toolsMenuFlyout.Closed += ToolsMenuFlyout_Closed;
                WireOverlayFlyout(_toolsMenuFlyout);
                _toolsMenuFlyout.ShowAt(ToolsButton, new FlyoutShowOptions
                {
                    Placement = FlyoutPlacementMode.TopEdgeAlignedRight
                });
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_teardownStarted || !_toolsPanelOpen)
                    {
                        return;
                    }

                    ToolsInfoButton.Focus(FocusState.Programmatic);
                });
                return;
            }

            RefreshToolToggleStates();
            UpdateToolsPanelVisibility();
            _toolsMenuFlyout?.Hide();
            _toolsMenuFlyout = null;
            SetMenuSurfaceInputShield(AreFlyoutMenusOpen, "tools_closed");
            RestorePlayerKeyboardFocus(force: true);
            ResumeOverlayAutoHide("tools_closed");
        }

        private void ToolsMenuFlyout_Closed(object? sender, object e)
        {
            if (!_toolsPanelOpen)
            {
                if (ReferenceEquals(sender, _toolsMenuFlyout))
                {
                    _toolsMenuFlyout = null;
                }

                return;
            }

            _toolsPanelOpen = false;
            if (ReferenceEquals(sender, _toolsMenuFlyout))
            {
                _toolsMenuFlyout = null;
            }

            RefreshToolToggleStates();
            UpdateToolsPanelVisibility();
        }

        private MenuFlyout CreateToolsMenuFlyout()
        {
            var flyout = new MenuFlyout();
            var isLive = IsLivePlayback();
            var isChannel = IsChannelPlayback();
            var canSeek = IsTimelineSeekAllowed();
            var hasEpisodeNavigation = _allEpisodeSwitchItems.Count > 1;
            var canUsePictureInPicture = CanUseFeature(EntitlementFeatureKeys.PlaybackPictureInPicture);

            AddToolsMenuItem(flyout.Items, "Info", () => TogglePanel(nameof(InfoPanel)), _infoPanelOpen);
            AddToolsMenuItem(flyout.Items, "Retry stream", RetryCurrentPlayback);
            if (!isLive && canSeek)
            {
                AddToolsMenuItem(flyout.Items, "Restart", () => RestartPlayerSession("tools_menu", 0));
            }

            if (IsCatchupPlayback() || (isLive && canSeek))
            {
                AddToolsMenuItem(flyout.Items, "Jump to live", () =>
                {
                    if (IsCatchupPlayback())
                    {
                        _ = ReturnToLivePlaybackAsync("tools_menu");
                    }
                    else
                    {
                        GoToLiveEdge("tools_menu");
                    }
                });
            }

            if (isChannel || hasEpisodeNavigation)
            {
                AddToolsSeparator(flyout.Items);
                if (isChannel)
                {
                    AddToolsMenuItem(flyout.Items, "Guide", () => TogglePanel(nameof(MiniGuidePanel)), _guidePanelOpen);
                    AddToolsMenuItem(flyout.Items, "Channels", () => TogglePanel(nameof(ChannelSwitchPanel)), _channelPanelOpen);
                }

                if (hasEpisodeNavigation)
                {
                    AddToolsMenuItem(flyout.Items, "Episodes", () => TogglePanel(nameof(EpisodePanel)), _episodePanelOpen);
                }
            }

            AddToolsSeparator(flyout.Items);
            AddDisplayToolsMenu(flyout.Items);
            AddAudioSubtitleToolsMenu(flyout.Items);
            AddSessionToolsMenu(flyout.Items, canUsePictureInPicture);
            return flyout;
        }

        private void AddDisplayToolsMenu(IList<MenuFlyoutItemBase> items)
        {
            var display = new MenuFlyoutSubItem { Text = "Display" };
            AddAspectMenuItem(display, "Automatic", PlaybackAspectMode.Automatic);
            AddAspectMenuItem(display, "Fill window", PlaybackAspectMode.FillWindow);
            AddAspectMenuItem(display, "16:9", PlaybackAspectMode.Ratio16x9);
            AddAspectMenuItem(display, "4:3", PlaybackAspectMode.Ratio4x3);
            AddAspectMenuItem(display, "1.85:1", PlaybackAspectMode.Ratio185x1);
            AddAspectMenuItem(display, "2.35:1", PlaybackAspectMode.Ratio235x1);
            display.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(display.Items, "Zoom out", () => AdjustVideoZoom(-0.15), isEnabled: _player != null);
            AddToolsMenuItem(display.Items, "Reset zoom", () => SetVideoZoom(0), IsApproximately(_player?.VideoZoom ?? 0, 0, 0.01), _player != null);
            AddToolsMenuItem(display.Items, "Zoom in", () => AdjustVideoZoom(0.15), isEnabled: _player != null);
            display.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(display.Items, "Rotation 0 deg", () => SetVideoRotation(0), NormalizeRotation(_player?.VideoRotation ?? 0) == 0, _player != null);
            AddToolsMenuItem(display.Items, "Rotation 90 deg", () => SetVideoRotation(90), NormalizeRotation(_player?.VideoRotation ?? 0) == 90, _player != null);
            AddToolsMenuItem(display.Items, "Rotation 180 deg", () => SetVideoRotation(180), NormalizeRotation(_player?.VideoRotation ?? 0) == 180, _player != null);
            AddToolsMenuItem(display.Items, "Rotation 270 deg", () => SetVideoRotation(270), NormalizeRotation(_player?.VideoRotation ?? 0) == 270, _player != null);
            display.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(display.Items, "Deinterlace", ToggleDeinterlace, _player?.IsDeinterlaceEnabled ?? _playerPreferences.Deinterlace, _player != null);
            items.Add(display);
        }

        private void AddAudioSubtitleToolsMenu(IList<MenuFlyoutItemBase> items)
        {
            var audioSubtitles = new MenuFlyoutSubItem { Text = "Audio and subtitles" };
            AddToolsMenuItem(audioSubtitles.Items, "Tracks", OpenTracksFlyout);
            audioSubtitles.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(audioSubtitles.Items, "Audio delay -0.1s", () => AdjustAudioDelay(-0.1));
            AddToolsMenuItem(audioSubtitles.Items, "Reset audio delay", () => SetAudioDelay(0), IsApproximately(_player?.AudioDelaySeconds ?? _playerPreferences.AudioDelaySeconds, 0, 0.01));
            AddToolsMenuItem(audioSubtitles.Items, "Audio delay +0.1s", () => AdjustAudioDelay(0.1));
            audioSubtitles.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(audioSubtitles.Items, "Subtitle delay -0.1s", () => AdjustSubtitleDelay(-0.1));
            AddToolsMenuItem(audioSubtitles.Items, "Reset subtitle delay", () => SetSubtitleDelay(0), IsApproximately(_player?.SubtitleDelaySeconds ?? _playerPreferences.SubtitleDelaySeconds, 0, 0.01));
            AddToolsMenuItem(audioSubtitles.Items, "Subtitle delay +0.1s", () => AdjustSubtitleDelay(0.1));
            audioSubtitles.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(audioSubtitles.Items, "Subtitle size small", () => SetSubtitleScale(0.85), IsApproximately(_player?.SubtitleScale > 0 ? _player.SubtitleScale : _playerPreferences.SubtitleScale, 0.85));
            AddToolsMenuItem(audioSubtitles.Items, "Subtitle size medium", () => SetSubtitleScale(1.0), IsApproximately(_player?.SubtitleScale > 0 ? _player.SubtitleScale : _playerPreferences.SubtitleScale, 1.0));
            AddToolsMenuItem(audioSubtitles.Items, "Subtitle size large", () => SetSubtitleScale(1.2), IsApproximately(_player?.SubtitleScale > 0 ? _player.SubtitleScale : _playerPreferences.SubtitleScale, 1.2));
            audioSubtitles.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(audioSubtitles.Items, "Subtitle high", () => SetSubtitlePosition(84), (_player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition) <= 88);
            AddToolsMenuItem(audioSubtitles.Items, "Subtitle middle", () => SetSubtitlePosition(92), (_player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition) > 88 && (_player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition) < 98);
            AddToolsMenuItem(audioSubtitles.Items, "Subtitle low", () => SetSubtitlePosition(100), (_player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition) >= 98);
            items.Add(audioSubtitles);
        }

        private void AddSessionToolsMenu(IList<MenuFlyoutItemBase> items, bool canUsePictureInPicture)
        {
            var session = new MenuFlyoutSubItem { Text = "Session" };
            AddToolsMenuItem(session.Items, "Sleep 30 minutes", () => SetSleepTimer(TimeSpan.FromMinutes(30)));
            AddToolsMenuItem(session.Items, "Sleep 60 minutes", () => SetSleepTimer(TimeSpan.FromMinutes(60)));
            AddToolsMenuItem(session.Items, "Sleep 90 minutes", () => SetSleepTimer(TimeSpan.FromMinutes(90)));
            AddToolsMenuItem(session.Items, "Sleep timer off", CancelSleepTimer, !_sleepDeadline.HasValue || _sleepDeadline.Value <= DateTimeOffset.UtcNow);
            session.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(session.Items, IsPictureInPictureMode() ? "Exit picture in picture" : "Picture in picture", () => PictureInPicture_Click(this, new RoutedEventArgs()), IsPictureInPictureMode(), canUsePictureInPicture);
            AddToolsMenuItem(session.Items, "Always on top", ToggleAlwaysOnTop, _windowManager?.IsAlwaysOnTop == true, !IsPictureInPictureMode());
            AddToolsMenuItem(session.Items, "Screenshot", CaptureScreenshot, isEnabled: _player != null);
            session.Items.Add(new MenuFlyoutSeparator());
            AddToolsMenuItem(session.Items, "Stop playback", () =>
            {
                _player?.Stop();
                NavigateBack();
            });
            items.Add(session);
        }

        private void AddAspectMenuItem(MenuFlyoutSubItem parent, string text, PlaybackAspectMode aspectMode)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = text,
                Tag = aspectMode,
                IsChecked = _selectedAspectMode == aspectMode,
                IsEnabled = CanUseFeature(EntitlementFeatureKeys.PlaybackAspectControls) && _player != null
            };
            item.Click += AspectItem_Click;
            parent.Items.Add(item);
        }

        private void AddToolsMenuItem(
            IList<MenuFlyoutItemBase> items,
            string text,
            Action action,
            bool? isChecked = null,
            bool isEnabled = true)
        {
            MenuFlyoutItem item = isChecked.HasValue
                ? new ToggleMenuFlyoutItem { Text = text, IsChecked = isChecked.Value }
                : new MenuFlyoutItem { Text = text };

            item.IsEnabled = isEnabled;
            item.Click += (_, _) =>
            {
                if (_teardownStarted)
                {
                    return;
                }

                action();
                RefreshToolToggleStates();
                RefreshInfoPanel();
                UpdatePlaybackHint();
                ResetInactivityTimer("tools_menu");
            };
            items.Add(item);
        }

        private static void AddToolsSeparator(IList<MenuFlyoutItemBase> items)
        {
            if (items.Count > 0 && items[^1] is not MenuFlyoutSeparator)
            {
                items.Add(new MenuFlyoutSeparator());
            }
        }

        private void ApplyToolButtonSelection(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.Background = selected ? ToolsButtonSelectedBackgroundBrush : ToolsButtonBackgroundBrush;
            button.BorderBrush = selected ? ToolsButtonSelectedBorderBrush : ToolsButtonBorderBrush;
            button.Foreground = selected ? ToolsButtonSelectedForegroundBrush : ToolsButtonForegroundBrush;
        }

        private static bool TryParseInvariantDouble(string rawValue, out double value)
        {
            return double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseInvariantInt(string rawValue, out int value)
        {
            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool IsApproximately(double value, double expected, double tolerance = 0.04)
        {
            return Math.Abs(value - expected) <= tolerance;
        }

        private static int NormalizeRotation(int rotation)
        {
            var normalized = rotation % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            return normalized;
        }

        private static string FormatSignedSeconds(double value)
        {
            return $"{value:+0.0;-0.0;0.0}s";
        }

        private static string GetAspectModeLabel(PlaybackAspectMode aspectMode)
        {
            return aspectMode switch
            {
                PlaybackAspectMode.FillWindow => "Fill window",
                PlaybackAspectMode.Ratio16x9 => "16:9",
                PlaybackAspectMode.Ratio4x3 => "4:3",
                PlaybackAspectMode.Ratio185x1 => "1.85",
                PlaybackAspectMode.Ratio235x1 => "2.35",
                _ => "Automatic"
            };
        }

        private string GetSubtitleSizeLabel(double scale)
        {
            var closest = _subtitleScalePresets
                .OrderBy(preset => Math.Abs(preset.Scale - scale))
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(closest.Label) ? "Medium" : closest.Label;
        }

        private static string GetSubtitlePositionLabel(int position)
        {
            if (position <= 88)
            {
                return "High";
            }

            if (position >= 98)
            {
                return "Low";
            }

            return "Mid";
        }

        private void RefreshToolToggleStates()
        {
            var zoom = _player?.VideoZoom ?? 0;
            var rotation = NormalizeRotation(_player?.VideoRotation ?? 0);
            var audioDelay = _player?.AudioDelaySeconds ?? _playerPreferences.AudioDelaySeconds;
            var subtitleDelay = _player?.SubtitleDelaySeconds ?? _playerPreferences.SubtitleDelaySeconds;
            var subtitleScale = _player?.SubtitleScale > 0 ? _player.SubtitleScale : _playerPreferences.SubtitleScale;
            var subtitlePosition = _player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition;
            var hasSleepTimer = _sleepDeadline.HasValue && _sleepDeadline.Value > DateTimeOffset.UtcNow;
            var sleepSummary = hasSleepTimer
                ? $"Sleep in {(_sleepDeadline!.Value - DateTimeOffset.UtcNow):hh\\:mm\\:ss}"
                : "Sleep timer off.";
            var windowSummary = _windowManager?.IsAlwaysOnTop == true ? "Always on top enabled." : "Window follows normal stacking.";

            ApplyToolButtonSelection(ToolsButton, _toolsPanelOpen);
            ApplyToolButtonSelection(ToolsGuideButton, _guidePanelOpen);
            ApplyToolButtonSelection(ToolsChannelsButton, _channelPanelOpen);
            ApplyToolButtonSelection(ToolsEpisodesButton, _episodePanelOpen);
            ApplyToolButtonSelection(ToolsInfoButton, _infoPanelOpen);
            ApplyToolButtonSelection(ToolsPictureInPictureButton, IsPictureInPictureMode());
            ApplyToolButtonSelection(ToolsAlwaysOnTopButton, _windowManager?.IsAlwaysOnTop == true);
            ApplyToolButtonSelection(ToolsDeinterlaceButton, _player?.IsDeinterlaceEnabled ?? _playerPreferences.Deinterlace);
            ApplyToolButtonSelection(AspectAutoButton, _selectedAspectMode == PlaybackAspectMode.Automatic);
            ApplyToolButtonSelection(AspectFillButton, _selectedAspectMode == PlaybackAspectMode.FillWindow);
            ApplyToolButtonSelection(Aspect169Button, _selectedAspectMode == PlaybackAspectMode.Ratio16x9);
            ApplyToolButtonSelection(Aspect43Button, _selectedAspectMode == PlaybackAspectMode.Ratio4x3);
            ApplyToolButtonSelection(Aspect185Button, _selectedAspectMode == PlaybackAspectMode.Ratio185x1);
            ApplyToolButtonSelection(Aspect235Button, _selectedAspectMode == PlaybackAspectMode.Ratio235x1);
            ApplyToolButtonSelection(ZoomResetButton, IsApproximately(zoom, 0, 0.01));
            ApplyToolButtonSelection(Rotation0Button, rotation == 0);
            ApplyToolButtonSelection(Rotation90Button, rotation == 90);
            ApplyToolButtonSelection(Rotation180Button, rotation == 180);
            ApplyToolButtonSelection(Rotation270Button, rotation == 270);
            ApplyToolButtonSelection(AudioDelayResetButton, IsApproximately(audioDelay, 0, 0.01));
            ApplyToolButtonSelection(SubtitleDelayResetButton, IsApproximately(subtitleDelay, 0, 0.01));
            ApplyToolButtonSelection(SubtitleSizeSmallButton, IsApproximately(subtitleScale, 0.85));
            ApplyToolButtonSelection(SubtitleSizeMediumButton, IsApproximately(subtitleScale, 1.0));
            ApplyToolButtonSelection(SubtitleSizeLargeButton, IsApproximately(subtitleScale, 1.2));
            ApplyToolButtonSelection(SubtitlePositionHighButton, subtitlePosition <= 88);
            ApplyToolButtonSelection(SubtitlePositionCenterButton, subtitlePosition > 88 && subtitlePosition < 98);
            ApplyToolButtonSelection(SubtitlePositionLowButton, subtitlePosition >= 98);
            ApplyToolButtonSelection(SleepTimerCancelButton, !hasSleepTimer);

            ToolsDisplaySummaryText.Text = $"{GetAspectModeLabel(_selectedAspectMode)} · Zoom {zoom:0.##} · Rotation {rotation} deg";
            ToolsAudioDelayValueText.Text = $"Audio delay {FormatSignedSeconds(audioDelay)}";
            ToolsSubtitleDelayValueText.Text = $"Subtitle delay {FormatSignedSeconds(subtitleDelay)}";
            ToolsSubtitleSummaryText.Text = $"Subtitle size {GetSubtitleSizeLabel(subtitleScale)} · Position {GetSubtitlePositionLabel(subtitlePosition)}";
            ToolsSessionSummaryText.Text = $"{sleepSummary} {windowSummary}";
        }

        private void BuildAudioDelayFlyout()
        {
            AudioDelayFlyout.Items.Clear();
            AddFlyoutItem(AudioDelayFlyout, "-0.1s", (_, _) => AdjustAudioDelay(-0.1));
            AddFlyoutItem(AudioDelayFlyout, "+0.1s", (_, _) => AdjustAudioDelay(0.1));
            AddFlyoutItem(AudioDelayFlyout, "Reset", (_, _) => SetAudioDelay(0));
        }

        private void BuildSubtitleDelayFlyout()
        {
            SubtitleDelayFlyout.Items.Clear();
            AddFlyoutItem(SubtitleDelayFlyout, "-0.1s", (_, _) => AdjustSubtitleDelay(-0.1));
            AddFlyoutItem(SubtitleDelayFlyout, "+0.1s", (_, _) => AdjustSubtitleDelay(0.1));
            AddFlyoutItem(SubtitleDelayFlyout, "Reset", (_, _) => SetSubtitleDelay(0));
        }

        private void BuildSubtitleStyleFlyout()
        {
            SubtitleStyleFlyout.Items.Clear();
            foreach (var preset in _subtitleScalePresets)
            {
                AddFlyoutItem(SubtitleStyleFlyout, preset.Label, (_, _) => SetSubtitleScale(preset.Scale));
            }

            AddFlyoutItem(SubtitleStyleFlyout, "High", (_, _) => SetSubtitlePosition(84));
            AddFlyoutItem(SubtitleStyleFlyout, "Mid", (_, _) => SetSubtitlePosition(92));
            AddFlyoutItem(SubtitleStyleFlyout, "Low", (_, _) => SetSubtitlePosition(100));
        }

        private void BuildZoomFlyout()
        {
            ZoomFlyout.Items.Clear();
            AddFlyoutItem(ZoomFlyout, "Zoom in", (_, _) => AdjustVideoZoom(0.15));
            AddFlyoutItem(ZoomFlyout, "Zoom out", (_, _) => AdjustVideoZoom(-0.15));
            AddFlyoutItem(ZoomFlyout, "Reset zoom", (_, _) => SetVideoZoom(0));
        }

        private void BuildRotationFlyout()
        {
            RotationFlyout.Items.Clear();
            AddFlyoutItem(RotationFlyout, "0 deg", (_, _) => SetVideoRotation(0));
            AddFlyoutItem(RotationFlyout, "90 deg", (_, _) => SetVideoRotation(90));
            AddFlyoutItem(RotationFlyout, "180 deg", (_, _) => SetVideoRotation(180));
            AddFlyoutItem(RotationFlyout, "270 deg", (_, _) => SetVideoRotation(270));
        }

        private void BuildSleepTimerFlyout()
        {
            SleepTimerFlyout.Items.Clear();
            AddFlyoutItem(SleepTimerFlyout, "30 minutes", (_, _) => SetSleepTimer(TimeSpan.FromMinutes(30)));
            AddFlyoutItem(SleepTimerFlyout, "60 minutes", (_, _) => SetSleepTimer(TimeSpan.FromMinutes(60)));
            AddFlyoutItem(SleepTimerFlyout, "90 minutes", (_, _) => SetSleepTimer(TimeSpan.FromMinutes(90)));
            AddFlyoutItem(SleepTimerFlyout, "Cancel", (_, _) => CancelSleepTimer());
        }

        private void AddFlyoutItem(MenuFlyout flyout, string text, RoutedEventHandler handler)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += handler;
            flyout.Items.Add(item);
        }

        private void OpenUtilityFlyout(MenuFlyout flyout)
        {
            if (_teardownStarted || flyout == null)
            {
                return;
            }

            if (ToolsButton.XamlRoot == null)
            {
                _pendingUtilityFlyout = null;
                return;
            }

            SuppressSurfaceClicks();
            SetMenuSurfaceInputShield(true, "utility_queue");
            var flyoutName = GetUtilityFlyoutLogName(flyout);
            LogPlaybackState($"OVERLAY: queue utility flyout name={flyoutName}");

            if (ReferenceEquals(_activeUtilityFlyout, flyout) && _openOverlayFlyouts.Contains(flyout))
            {
                flyout.Hide();
                return;
            }

            if (_activeUtilityFlyout != null && !ReferenceEquals(_activeUtilityFlyout, flyout))
            {
                _activeUtilityFlyout.Hide();
            }

            _pendingUtilityFlyout = flyout;
            ShowControls(persist: true, cause: "utility_menu_open");
            CloseToolsPanel();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_pendingUtilityFlyout, flyout))
                {
                    LogPlaybackState($"OVERLAY: utility flyout callback skipped name={flyoutName} reason=stale");
                    return;
                }

                if (_teardownStarted || ToolsButton.XamlRoot == null)
                {
                    LogPlaybackState($"OVERLAY: utility flyout callback skipped name={flyoutName} reason=teardown");
                    if (ReferenceEquals(_pendingUtilityFlyout, flyout))
                    {
                        _pendingUtilityFlyout = null;
                    }

                    return;
                }

                LogPlaybackState($"OVERLAY: utility flyout showing name={flyoutName}");
                flyout.ShowAt(ToolsButton, new FlyoutShowOptions
                {
                    Placement = FlyoutPlacementMode.TopEdgeAlignedRight
                });
            });
        }

        private string GetUtilityFlyoutLogName(MenuFlyout flyout)
        {
            if (ReferenceEquals(flyout, AspectFlyout)) return "display";
            if (ReferenceEquals(flyout, AudioDelayFlyout)) return "audio_delay";
            if (ReferenceEquals(flyout, SubtitleDelayFlyout)) return "subtitle_delay";
            if (ReferenceEquals(flyout, SubtitleStyleFlyout)) return "subtitle_style";
            if (ReferenceEquals(flyout, ZoomFlyout)) return "zoom";
            if (ReferenceEquals(flyout, RotationFlyout)) return "rotation";
            if (ReferenceEquals(flyout, SleepTimerFlyout)) return "sleep_timer";
            return "unknown";
        }

        private void AddAspectSubItem(MenuFlyoutSubItem parent, string text, PlaybackAspectMode aspectMode)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = text,
                Tag = aspectMode,
                IsChecked = _selectedAspectMode == aspectMode
            };

            item.Click += AspectItem_Click;
            parent.Items.Add(item);
        }

        private void RefreshSpeedUi()
        {
            var speed = _player?.PlaybackSpeed > 0 ? _player.PlaybackSpeed : _playerPreferences.PlaybackSpeed;
            SpeedButtonText.Text = $"{speed:0.##}x";
            foreach (var (value, item) in _speedItems)
            {
                item.IsChecked = Math.Abs(value - speed) < 0.01;
            }
        }

        private void UpdateFavoriteUi()
        {
            FavoriteIcon.Glyph = _isFavorite ? "\uE735" : "\uE734";
            ToolTipService.SetToolTip(FavoriteButton, _isFavorite ? "Remove from saved items" : "Save this item");
        }

        private void UpdatePanelVisibility()
        {
            MiniGuidePanel.Visibility = _guidePanelOpen ? Visibility.Visible : Visibility.Collapsed;
            ChannelSwitchPanel.Visibility = _channelPanelOpen ? Visibility.Visible : Visibility.Collapsed;
            EpisodePanel.Visibility = _episodePanelOpen ? Visibility.Visible : Visibility.Collapsed;
            InfoPanel.Visibility = _infoPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TogglePanel(string panelName)
        {
            var channelPanelWasOpen = _channelPanelOpen;
            _guidePanelOpen = panelName == nameof(MiniGuidePanel) && !_guidePanelOpen;
            _channelPanelOpen = panelName == nameof(ChannelSwitchPanel) && !_channelPanelOpen;
            _episodePanelOpen = panelName == nameof(EpisodePanel) && !_episodePanelOpen;
            _infoPanelOpen = panelName == nameof(InfoPanel) && !_infoPanelOpen;
            if (channelPanelWasOpen && !_channelPanelOpen)
            {
                ResetChannelSearchPanel();
            }

            UpdatePanelVisibility();
            RefreshToolToggleStates();
            if (_guidePanelOpen || _channelPanelOpen || _episodePanelOpen || _infoPanelOpen)
            {
                FocusActivePanelTarget();
            }
            else
            {
                RestorePlayerKeyboardFocus(force: true);
            }

            ShowControls(persist: _guidePanelOpen || _channelPanelOpen || _episodePanelOpen || _infoPanelOpen, cause: "panel_toggle");
        }

        private void FocusActivePanelTarget()
        {
            if (_guidePanelOpen)
            {
                if (GuideProgramList.Items.Count > 0 &&
                    GuideProgramList.ContainerFromIndex(0) is ListViewItem firstGuideItem &&
                    firstGuideItem.Focus(FocusState.Keyboard))
                {
                    return;
                }

                if (GuideProgramList.Focus(FocusState.Keyboard))
                {
                    return;
                }
            }

            if (_channelPanelOpen)
            {
                if (ChannelSearchBox.Focus(FocusState.Keyboard))
                {
                    return;
                }
            }

            if (_episodePanelOpen)
            {
                if (EpisodeSwitchList.Items.Count > 0 &&
                    EpisodeSwitchList.ContainerFromIndex(0) is ListViewItem firstEpisodeItem &&
                    firstEpisodeItem.Focus(FocusState.Keyboard))
                {
                    return;
                }

                if (EpisodeSwitchList.Focus(FocusState.Keyboard))
                {
                    return;
                }
            }

            if (_infoPanelOpen)
            {
                if (InspectCurrentItemButton.Focus(FocusState.Keyboard) ||
                    OpenExternalPlayerButton.Focus(FocusState.Keyboard))
                {
                    return;
                }
            }

            RootGrid.Focus(FocusState.Keyboard);
        }

        private bool CloseOpenPanels()
        {
            if (!_guidePanelOpen && !_channelPanelOpen && !_episodePanelOpen && !_infoPanelOpen)
            {
                return false;
            }

            ResetTransientPlaybackPanels(clearChannelSearch: true, restoreFocus: true);
            return true;
        }

        private void ResetTransientPlaybackPanels(bool clearChannelSearch, bool restoreFocus)
        {
            var hadOpenPanels = _guidePanelOpen || _channelPanelOpen || _episodePanelOpen || _infoPanelOpen;
            _guidePanelOpen = false;
            _channelPanelOpen = false;
            _episodePanelOpen = false;
            _infoPanelOpen = false;
            UpdatePanelVisibility();

            if (clearChannelSearch)
            {
                ResetChannelSearchPanel();
            }

            RefreshToolToggleStates();
            if (restoreFocus && hadOpenPanels)
            {
                RestorePlayerKeyboardFocus(force: true);
            }
        }

        private void ResetChannelSearchPanel()
        {
            if (ChannelSearchBox == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(ChannelSearchBox.Text))
            {
                ChannelSearchBox.Text = string.Empty;
                return;
            }

            ApplyChannelSearchFilter();
        }

        private void ApplyChannelSearchFilter()
        {
            var query = ChannelSearchBox?.Text?.Trim() ?? string.Empty;
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allChannelSwitchItems
                : _allChannelSwitchItems.Where(item =>
                    item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    item.SourceName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    item.MetaText.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

            _channelSwitchItems.Clear();
            foreach (var item in filtered)
            {
                _channelSwitchItems.Add(item);
            }

            ChannelSearchStatusText.Text = filtered.Count == 0
                ? "No channels match your search."
                : $"{filtered.Count:N0} channels ready.";
        }

        private void ApplyEpisodeFilter()
        {
            _episodeSwitchItems.Clear();
            foreach (var item in _allEpisodeSwitchItems)
            {
                _episodeSwitchItems.Add(item);
            }
        }

        private void UpdatePlaybackHint()
        {
            var hints = new List<string>();
            if (IsLivePlayback() && !IsTimelineSeekAllowed())
            {
                hints.Add("Seek unavailable on this live stream.");
            }

            if (IsCatchupPlayback() && !string.IsNullOrWhiteSpace(_context?.CatchupStatusText))
            {
                hints.Add(_context.CatchupStatusText);
            }

            if (!string.IsNullOrWhiteSpace(_context?.OperationalSummary))
            {
                hints.Add(ToPlayerFacingPlaybackHint(_context.OperationalSummary));
            }
            else if (!string.IsNullOrWhiteSpace(_resolvedRoutingSummary) && !string.Equals(_resolvedRoutingSummary, "Direct routing", StringComparison.OrdinalIgnoreCase))
            {
                hints.Add(ToPlayerFacingPlaybackHint(_resolvedRoutingSummary));
            }

            if (_sleepDeadline.HasValue)
            {
                var remaining = _sleepDeadline.Value - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    hints.Add($"Sleep timer {remaining:hh\\:mm\\:ss}");
                }
            }

            PlaybackHintText.Text = string.Join("  •  ", hints);
            PlaybackHintText.Visibility = string.IsNullOrWhiteSpace(PlaybackHintText.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string ToPlayerFacingPlaybackHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("Probe-backed", "Playback ready", StringComparison.OrdinalIgnoreCase)
                .Replace("weak probe confidence", "connection may vary", StringComparison.OrdinalIgnoreCase)
                .Replace("guide mapped", "guide available", StringComparison.OrdinalIgnoreCase)
                .Replace("logo ready", "logo available", StringComparison.OrdinalIgnoreCase)
                .Replace("poster", "artwork available", StringComparison.OrdinalIgnoreCase)
                .Replace("overview", "description available", StringComparison.OrdinalIgnoreCase)
                .Replace("metadata", "metadata available", StringComparison.OrdinalIgnoreCase)
                .Replace("stale source", "refresh suggested", StringComparison.OrdinalIgnoreCase)
                .Replace("proxy routed", "optimized route", StringComparison.OrdinalIgnoreCase)
                .Replace("Operationally usable live mirror", "Ready live source", StringComparison.OrdinalIgnoreCase)
                .Replace("Operationally usable movie source", "Ready movie source", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshInfoPanel()
        {
            var info = _player?.GetPlaybackInfoSnapshot() ?? new Services.Playback.MpvPlaybackInfoSnapshot();
            InfoSummaryText.Text = _stateMachine.State == PlaybackSessionState.Error
                ? _lastStateMessage
                : !string.IsNullOrWhiteSpace(_context?.OperationalSummary)
                    ? _context.OperationalSummary
                    : IsCatchupPlayback()
                        ? "Catchup replay is active on the live channel path."
                        : IsLivePlayback() ? "Live playback tools reflect real stream state and guide confidence." : "Seekable playback controls are active for this title.";
            InfoResolutionText.Text = info.HasVideo ? $"{info.Width}x{info.Height}" : "Unknown";
            InfoFpsText.Text = info.FramesPerSecond > 0 ? $"{info.FramesPerSecond:0.##}" : "Unknown";
            InfoVideoCodecText.Text = string.IsNullOrWhiteSpace(info.VideoCodec) ? "Unknown" : info.VideoCodec;
            InfoAudioCodecText.Text = string.IsNullOrWhiteSpace(info.AudioCodec) ? "Unknown" : info.AudioCodec;
            InfoSourceText.Text = string.IsNullOrWhiteSpace(_resolvedSourceName)
                ? string.IsNullOrWhiteSpace(_resolvedRoutingSummary) ? "Unknown" : _resolvedRoutingSummary
                : string.IsNullOrWhiteSpace(_resolvedRoutingSummary) || string.Equals(_resolvedRoutingSummary, "Direct routing", StringComparison.OrdinalIgnoreCase)
                    ? _resolvedSourceName
                    : $"{_resolvedSourceName} · {_resolvedRoutingSummary}";
            InfoSpeedText.Text = $"{(_player?.PlaybackSpeed > 0 ? _player.PlaybackSpeed : _playerPreferences.PlaybackSpeed):0.##}x";
            InfoSeekText.Text = IsCatchupPlayback()
                ? IsTimelineSeekAllowed() ? "Catchup replay" : "Catchup not seekable"
                : IsTimelineSeekAllowed() ? "Seekable" : "Not seekable";
            InfoGuideText.Text = IsCatchupPlayback() && !string.IsNullOrWhiteSpace(_context?.CatchupStatusText)
                ? _context.CatchupStatusText
                : string.IsNullOrWhiteSpace(_resolvedGuideSummary) ? "No guide status" : _resolvedGuideSummary;
        }

        private async void InspectCurrentItem_Click(object sender, RoutedEventArgs e)
        {
            if (_context == null || XamlRoot == null)
            {
                return;
            }

            await ItemInspectorDialog.ShowAsync(
                XamlRoot,
                _context.Clone(),
                BuildInspectionRuntimeState());
        }

        private async void OpenInExternalPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (_context == null)
            {
                return;
            }

            if (sender is Control control)
            {
                control.IsEnabled = false;
                try
                {
                    var service = ((App)Application.Current).Services.GetRequiredService<IExternalPlayerLaunchService>();
                    var result = await service.LaunchAsync(_context.Clone(), preferCurrentResolvedStream: true);
                    if (result.Success)
                    {
                        ShowZapBanner(TitleText.Text, result.Message);
                    }
                    else
                    {
                        ShowZapBanner("External player unavailable", result.Message);
                    }
                }
                finally
                {
                    control.IsEnabled = true;
                }

                return;
            }

            var externalPlayerLaunchService = ((App)Application.Current).Services.GetRequiredService<IExternalPlayerLaunchService>();
            var launchResult = await externalPlayerLaunchService.LaunchAsync(_context.Clone(), preferCurrentResolvedStream: true);
            ShowZapBanner(
                launchResult.Success ? TitleText.Text : "External player unavailable",
                launchResult.Message);
        }

        private PlayableItemInspectionRuntimeState BuildInspectionRuntimeState()
        {
            var info = _player?.GetPlaybackInfoSnapshot() ?? new Services.Playback.MpvPlaybackInfoSnapshot();
            return new PlayableItemInspectionRuntimeState
            {
                IsCurrentPlayback = true,
                SessionState = _stateMachine.State.ToString(),
                SessionMessage = _lastStateMessage,
                Width = info.Width,
                Height = info.Height,
                FramesPerSecond = info.FramesPerSecond,
                VideoCodec = info.VideoCodec,
                AudioCodec = info.AudioCodec,
                ContainerFormat = info.ContainerFormat,
                PixelFormat = info.PixelFormat,
                IsHardwareDecodingActive = info.IsHardwareDecodingActive,
                PositionMs = _lastPositionMs,
                DurationMs = _lastDurationMs,
                IsSeekable = IsTimelineSeekAllowed()
            };
        }

        private void RetryCurrentPlayback()
        {
            var retryPosition = IsLivePlayback() ? 0 : GetRetryPositionMs();
            RestartPlayerSession("manual_retry", retryPosition);
        }

        private void RestartPlayerSession(string reason, long startPositionMs)
        {
            if (_surface == null)
            {
                ResetSessionRuntimeState($"restart:{reason}", startPositionMs, clearRetryState: true);
                BeginPlaybackAttempt(isRetry: false, retryReason: reason, startPositionMs: startPositionMs);
                return;
            }

            StopLoadTimeout();
            StopBufferTimeout();
            _progressPersistTimer?.Stop();
            ResetTransientPlaybackPanels(clearChannelSearch: true, restoreFocus: false);
            ResetSessionRuntimeState($"restart:{reason}", startPositionMs, clearRetryState: true);
            DetachAndDisposePlayer();
            _player = CreatePlayer(_surface.Handle);
            _launchOverridesApplied = false;
            ResetTrackMenus();
            ResetResolvedPlaybackUiState();
            ClearError();
            HideStatusOverlay();
            BeginPlaybackAttempt(isRetry: false, retryReason: reason, startPositionMs: _lastPositionMs);
            UpdateEnhancedControlState();
        }

        private async Task RecordChannelLaunchAsync(int channelId)
        {
            if (_context == null || channelId <= 0)
            {
                return;
            }

            using var scope = ((App)Application.Current).Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var logicalCatalogStateService = scope.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
            await logicalCatalogStateService.RecordLiveChannelLaunchAsync(db, _context.ProfileId, channelId);
        }

        private async Task SwitchToChannelAsync(int channelId, string reason)
        {
            var nextItem = _allChannelSwitchItems.FirstOrDefault(item => item.Id == channelId);
            if (_context == null || nextItem == null || string.IsNullOrWhiteSpace(nextItem.StreamUrl))
            {
                return;
            }

            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;
            var switchGeneration = BeginContextSwitch($"channel_switch:{reason}");
            _failedMirrorContentIds.Clear();
            await RecordChannelLaunchAsync(channelId);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken) || _context == null)
            {
                return;
            }

            ResetResolvedTransportState(_context);
            _context.ContentId = nextItem.Id;
            _context.ContentType = PlaybackContentType.Channel;
            _context.LogicalContentKey = nextItem.LogicalContentKey;
            _context.PreferredSourceProfileId = nextItem.PreferredSourceProfileId;
            ResetCatchupContext(_context, nextItem.StreamUrl);
            _context.RestoreAudioTrackSelection = false;
            _context.RestoreSubtitleTrackSelection = false;
            _context.PreferredAudioTrackId = string.Empty;
            _context.PreferredSubtitleTrackId = string.Empty;

            TitleText.Text = nextItem.Name;
            ShowZapBanner(nextItem.Name, nextItem.MetaText);
            ResetResolvedPlaybackUiState();
            await LoadEnhancedPlayerStateAsync(cancellationToken);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken))
            {
                return;
            }

            RestartPlayerSession($"channel_switch:{reason}", 0);
        }

        private async Task SwitchToEpisodeAsync(int episodeId)
        {
            var nextItem = _allEpisodeSwitchItems.FirstOrDefault(item => item.Id == episodeId);
            if (_context == null || nextItem == null || string.IsNullOrWhiteSpace(nextItem.StreamUrl))
            {
                return;
            }

            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;
            var switchGeneration = BeginContextSwitch("episode_switch");
            _failedMirrorContentIds.Clear();
            ResetResolvedTransportState(_context);
            _context.ContentId = nextItem.Id;
            _context.ContentType = PlaybackContentType.Episode;
            _context.LogicalContentKey = string.Empty;
            _context.PreferredSourceProfileId = 0;
            _context.CatalogStreamUrl = nextItem.StreamUrl;
            _context.StreamUrl = nextItem.StreamUrl;
            _context.StartPositionMs = nextItem.ResumePositionMs;
            TitleText.Text = nextItem.Title;
            ResetResolvedPlaybackUiState();
            await LoadEnhancedPlayerStateAsync(cancellationToken);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken))
            {
                return;
            }

            RestartPlayerSession("episode_switch", nextItem.ResumePositionMs);
        }

        private async Task PlayGuideProgramAsync(PlayerGuideProgramItem item)
        {
            if (_context == null || !IsChannelPlayback())
            {
                return;
            }

            if (item.RequestKind == CatchupRequestKind.None)
            {
                if (!string.IsNullOrWhiteSpace(item.StatusText))
                {
                    ShowZapBanner(item.Title, item.StatusText);
                }

                return;
            }

            var nextContext = _context.Clone();
            ResetResolvedTransportState(nextContext);
            nextContext.PlaybackMode = CatchupPlaybackMode.Catchup;
            nextContext.CatchupRequestKind = item.RequestKind;
            nextContext.CatchupProgramTitle = item.Title.Replace("Now · ", string.Empty, StringComparison.Ordinal);
            nextContext.CatchupProgramStartTimeUtc = item.StartTimeUtc;
            nextContext.CatchupProgramEndTimeUtc = item.EndTimeUtc;
            nextContext.CatchupRequestedAtUtc = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(nextContext.LiveStreamUrl))
            {
                nextContext.LiveStreamUrl = IsCatchupPlayback() ? _context.LiveStreamUrl : _context.StreamUrl;
            }

            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;
            var switchGeneration = BeginContextSwitch(item.RequestKind == CatchupRequestKind.StartOver ? "catchup_start_over" : "catchup_replay");
            var resolution = await ResolveCatchupContextAsync(nextContext, cancellationToken);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken))
            {
                return;
            }

            if (!resolution.Success)
            {
                ShowZapBanner(item.Title, resolution.Message);
                RefreshInfoPanel();
                UpdatePlaybackHint();
                return;
            }

            _failedMirrorContentIds.Clear();
            _context = nextContext;
            ShowZapBanner(item.Title, resolution.Message);
            ResetResolvedPlaybackUiState();
            await LoadEnhancedPlayerStateAsync(cancellationToken);
            if (!IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken))
            {
                return;
            }

            if (!await ResolveCatchupContextIfNeededAsync(cancellationToken) ||
                !IsPlaybackContextOperationActive(playbackSessionId, switchGeneration, cancellationToken))
            {
                return;
            }

            RestartPlayerSession(item.RequestKind == CatchupRequestKind.StartOver ? "catchup_start_over" : "catchup_replay", 0);
        }

        private void ShowZapBanner(string title, string message)
        {
            ZapBannerTitleText.Text = title;
            ZapBannerMessageText.Text = message;
            ZapBanner.Visibility = Visibility.Visible;
            _zapBannerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _zapBannerTimer.Stop();
            _zapBannerTimer.Tick -= ZapBannerTimer_Tick;
            _zapBannerTimer.Tick += ZapBannerTimer_Tick;
            _zapBannerTimer.Start();
        }

        private void HideZapBanner()
        {
            _zapBannerTimer?.Stop();
            ZapBanner.Visibility = Visibility.Collapsed;
            ZapBannerTitleText.Text = string.Empty;
            ZapBannerMessageText.Text = string.Empty;
        }

        private void ZapBannerTimer_Tick(object? sender, object e)
        {
            HideZapBanner();
        }

        private DependencyObject? GetFocusedElement()
        {
            return FocusManager.GetFocusedElement(XamlRoot) as DependencyObject ??
                   FocusManager.GetFocusedElement() as DependencyObject;
        }

        private bool IsTextInputFocused()
        {
            var current = GetFocusedElement();
            while (current != null)
            {
                if (current is TextBox or PasswordBox or AutoSuggestBox or RichEditBox)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private bool ShouldSuppressGlobalHotkeys(KeyRoutedEventArgs e)
        {
            if (IsMenuOpen && e.Key != VirtualKey.Escape)
            {
                return true;
            }

            return e.Key != VirtualKey.Escape && IsTextInputFocused();
        }

        private bool ShouldReserveFocusedControlKeys()
        {
            var current = GetFocusedElement();
            while (current != null)
            {
                if (ReferenceEquals(current, RootGrid))
                {
                    return false;
                }

                if (current is ButtonBase or HyperlinkButton or ToggleSwitch or CheckBox or ComboBox or ComboBoxItem or Slider or ListViewItem or GridViewItem)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private bool CloseTopmostOverlayFlyout()
        {
            if (_pendingUtilityFlyout != null && _activeUtilityFlyout == null)
            {
                _pendingUtilityFlyout = null;
                if (_openOverlayFlyouts.Count > 0)
                {
                    _openOverlayFlyouts[^1].Hide();
                }

                return true;
            }

            if (_activeUtilityFlyout != null)
            {
                _activeUtilityFlyout.Hide();
                return true;
            }

            if (_openOverlayFlyouts.Count == 0)
            {
                return false;
            }

            _openOverlayFlyouts[^1].Hide();
            return true;
        }

        private bool HandleEscapeHotkey()
        {
            if (CloseTopmostOverlayFlyout())
            {
                ShowControls(persist: true, cause: "menu_closed");
                return true;
            }

            if (CloseToolsPanel())
            {
                ShowControls(persist: true, cause: "tools_closed");
                return true;
            }

            if (CloseOpenPanels())
            {
                ShowControls(cause: "panel_closed");
                return true;
            }

            if (_isOverlayVisible)
            {
                _overlayHiddenByInactivity = true;
                HideControls();
                return true;
            }

            if (_windowManager?.IsFullscreen == true)
            {
                _windowManager.ExitFullscreen();
                ShowControls(cause: "fullscreen_exit");
                return true;
            }

            return false;
        }

        private void OpenTracksFlyout()
        {
            if (TracksButton.Visibility == Visibility.Visible)
            {
                TracksButton.Flyout?.ShowAt(TracksButton);
                return;
            }

            if (AudioTrackButton.Visibility == Visibility.Visible)
            {
                AudioTrackButton.Flyout?.ShowAt(AudioTrackButton);
                return;
            }

            if (SubtitleTrackButton.Visibility == Visibility.Visible)
            {
                SubtitleTrackButton.Flyout?.ShowAt(SubtitleTrackButton);
            }
        }

        private void TrySeekRelativeSeconds(int seconds)
        {
            if (!IsTimelineSeekAllowed())
            {
                return;
            }

            _player?.SeekRelativeSeconds(seconds);
            if (IsLivePlayback())
            {
                _isLiveTimeshiftActive = true;
            }
        }

        private bool HandleEnhancedKeyDown(KeyRoutedEventArgs e)
        {
            if (ShouldSuppressGlobalHotkeys(e))
            {
                return false;
            }

            var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            var context = new PlaybackRemoteContext
            {
                IsTextInputFocused = IsTextInputFocused(),
                IsMenuOpen = IsMenuOpen,
                ReserveFocusedControlKeys = ShouldReserveFocusedControlKeys(),
                IsPictureInPicture = IsPictureInPictureMode(),
                IsLivePlayback = IsLivePlayback(),
                IsChannelPlayback = IsChannelPlayback(),
                CanSeek = IsTimelineSeekAllowed(),
                HasLastChannel = _lastChannelCandidateId > 0,
                CanRestartOrStartOver = IsTimelineSeekAllowed() || (IsChannelPlayback() && _guideProgramItems.Any(item => item.IsCurrent && item.RequestKind != CatchupRequestKind.None)),
                CanGoLive = IsChannelPlayback()
            };

            var command = PlaybackRemoteCommandMap.Resolve(e.Key, shiftDown, context);
            if (command == PlaybackRemoteCommand.None)
            {
                return false;
            }

            switch (command)
            {
                case PlaybackRemoteCommand.TogglePlayPause:
                    TogglePlayPauseOrLive();
                    break;
                case PlaybackRemoteCommand.SeekBackward10:
                    TrySeekRelativeSeconds(-10);
                    break;
                case PlaybackRemoteCommand.SeekBackward30:
                    TrySeekRelativeSeconds(-30);
                    break;
                case PlaybackRemoteCommand.SeekForward10:
                    TrySeekRelativeSeconds(10);
                    break;
                case PlaybackRemoteCommand.SeekForward30:
                    TrySeekRelativeSeconds(30);
                    break;
                case PlaybackRemoteCommand.VolumeUp:
                    AdjustVolume(5);
                    break;
                case PlaybackRemoteCommand.VolumeDown:
                    AdjustVolume(-5);
                    break;
                case PlaybackRemoteCommand.PreviousChannel:
                    _ = SwitchRelativeChannelAsync(-1);
                    break;
                case PlaybackRemoteCommand.NextChannel:
                    _ = SwitchRelativeChannelAsync(1);
                    break;
                case PlaybackRemoteCommand.LastChannel:
                    _ = SwitchToChannelAsync(_lastChannelCandidateId, "last_hotkey");
                    break;
                case PlaybackRemoteCommand.ToggleFullscreen:
                    RequestFullscreenToggle("hotkey");
                    break;
                case PlaybackRemoteCommand.ToggleMute:
                    Mute_Click(this, new RoutedEventArgs());
                    break;
                case PlaybackRemoteCommand.ToggleSubtitles:
                    ToggleSubtitleSelection();
                    break;
                case PlaybackRemoteCommand.OpenTrackSelection:
                    OpenTracksFlyout();
                    break;
                case PlaybackRemoteCommand.ToggleInfoPanel:
                    TogglePanel(nameof(InfoPanel));
                    break;
                case PlaybackRemoteCommand.ToggleGuidePanel:
                    TogglePanel(nameof(MiniGuidePanel));
                    break;
                case PlaybackRemoteCommand.RestartOrStartOver:
                    _ = TryRestartOrStartOverAsync();
                    break;
                case PlaybackRemoteCommand.GoLive:
                    if (IsCatchupPlayback())
                    {
                        _ = ReturnToLivePlaybackAsync("hotkey");
                    }
                    else
                    {
                        GoToLiveEdge("hotkey");
                    }
                    break;
                case PlaybackRemoteCommand.StopPlayback:
                    Stop_Click(this, new RoutedEventArgs());
                    break;
                case PlaybackRemoteCommand.CloseContext:
                    if (!HandleEscapeHotkey())
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }

            ResetInactivityTimer("key_input");
            RefreshInfoPanel();
            UpdatePlaybackHint();
            e.Handled = true;
            return true;
        }

        private async Task TryRestartOrStartOverAsync()
        {
            if (_context == null)
            {
                return;
            }

            if (IsChannelPlayback())
            {
                var currentProgram = _guideProgramItems.FirstOrDefault(item => item.IsCurrent && item.RequestKind != CatchupRequestKind.None);
                if (currentProgram != null)
                {
                    await PlayGuideProgramAsync(currentProgram);
                    return;
                }
            }

            if (IsTimelineSeekAllowed())
            {
                RestartPlayerSession("remote_restart", 0);
            }
        }

        private async Task SwitchRelativeChannelAsync(int delta)
        {
            if (_context == null || _allChannelSwitchItems.Count == 0)
            {
                return;
            }

            var currentIndex = _allChannelSwitchItems.FindIndex(item => item.Id == _context.ContentId);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var targetIndex = (currentIndex + delta + _allChannelSwitchItems.Count) % _allChannelSwitchItems.Count;
            await SwitchToChannelAsync(_allChannelSwitchItems[targetIndex].Id, delta > 0 ? "next" : "previous");
        }

        private void ToggleSubtitleSelection()
        {
            if (_player == null)
            {
                return;
            }

            var selectedTrackId = GetSelectedTrackId(_player.GetSubtitleTracks());
            if (!string.IsNullOrWhiteSpace(selectedTrackId))
            {
                _player.SelectSubtitleTrack(string.Empty);
                if (_context != null)
                {
                    _context.PreferredSubtitleTrackId = string.Empty;
                    _context.SubtitlesEnabled = false;
                }
            }
            else
            {
                var track = _player.GetSubtitleTracks().FirstOrDefault();
                if (track == null)
                {
                    return;
                }

                _player.SelectSubtitleTrack(track.Id);
                if (_context != null)
                {
                    _context.PreferredSubtitleTrackId = track.Id;
                    _context.SubtitlesEnabled = true;
                }
            }

            _ = RefreshTrackMenusAsync();
            _ = SavePlayerPreferencesAsync();
        }

        private void AdjustVolume(double delta)
        {
            var next = Math.Clamp(VolumeSlider.Value + delta, 0, 100);
            SetVolumeSliderValue(next);
            _player?.SetVolume(next);
            _player?.SetMuted(next <= 0);
            SetMutedUi(next <= 0);
        }

        private void ToggleAlwaysOnTop()
        {
            if (IsPictureInPictureMode())
            {
                return;
            }

            _windowManager?.SetAlwaysOnTop(!(_windowManager?.IsAlwaysOnTop == true));
            RefreshToolToggleStates();
            _ = SavePlayerPreferencesAsync();
        }

        private void ToggleDeinterlace()
        {
            if (_player == null)
            {
                return;
            }

            LogPlaybackState($"TOOL: deinterlace toggle next={BoolToLog(!_player.IsDeinterlaceEnabled)}");
            _player.SetDeinterlace(!_player.IsDeinterlaceEnabled);
            RefreshToolToggleStates();
            _ = SavePlayerPreferencesAsync();
        }

        private void ToolsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted)
            {
                return;
            }

            foreach (var flyout in _openOverlayFlyouts.ToArray())
            {
                try { flyout.Hide(); } catch { }
            }

            _activeUtilityFlyout = null;
            _pendingUtilityFlyout = null;
            SetToolsPanelOpen(!_toolsPanelOpen, "button");
            ResetInactivityTimer("click");
        }

        private void ToggleAlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            ToggleAlwaysOnTop();
            ResetInactivityTimer("click");
        }

        private void ToggleDeinterlace_Click(object sender, RoutedEventArgs e)
        {
            ToggleDeinterlace();
            ResetInactivityTimer("click");
        }

        private void CaptureScreenshot_Click(object sender, RoutedEventArgs e)
        {
            CaptureScreenshot();
            ResetInactivityTimer("click");
        }

        private void AspectToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanUseFeature(EntitlementFeatureKeys.PlaybackAspectControls) ||
                _player == null ||
                sender is not Button { Tag: string rawAspect } ||
                !Enum.TryParse(rawAspect, out PlaybackAspectMode aspectMode))
            {
                return;
            }

            _selectedAspectMode = aspectMode;
            _player.SetAspectMode(aspectMode);
            UpdateAspectUi();
            BuildToolsFlyout();
            _ = SavePlayerPreferencesAsync();
            RefreshInfoPanel();
            ResetInactivityTimer("click");
        }

        private void AudioDelayTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string rawValue })
            {
                return;
            }

            if (string.Equals(rawValue, "reset", StringComparison.OrdinalIgnoreCase))
            {
                SetAudioDelay(0);
            }
            else if (TryParseInvariantDouble(rawValue, out var delta))
            {
                AdjustAudioDelay(delta);
            }

            ResetInactivityTimer("click");
        }

        private void SubtitleDelayTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string rawValue })
            {
                return;
            }

            if (string.Equals(rawValue, "reset", StringComparison.OrdinalIgnoreCase))
            {
                SetSubtitleDelay(0);
            }
            else if (TryParseInvariantDouble(rawValue, out var delta))
            {
                AdjustSubtitleDelay(delta);
            }

            ResetInactivityTimer("click");
        }

        private void SubtitleStyleTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string rawValue })
            {
                return;
            }

            if (rawValue.StartsWith("scale:", StringComparison.OrdinalIgnoreCase) &&
                TryParseInvariantDouble(rawValue["scale:".Length..], out var scale))
            {
                SetSubtitleScale(scale);
            }
            else if (rawValue.StartsWith("position:", StringComparison.OrdinalIgnoreCase) &&
                     TryParseInvariantInt(rawValue["position:".Length..], out var position))
            {
                SetSubtitlePosition(position);
            }

            ResetInactivityTimer("click");
        }

        private void ZoomTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string rawValue })
            {
                return;
            }

            if (string.Equals(rawValue, "reset", StringComparison.OrdinalIgnoreCase))
            {
                SetVideoZoom(0);
            }
            else if (TryParseInvariantDouble(rawValue, out var delta))
            {
                AdjustVideoZoom(delta);
            }

            ResetInactivityTimer("click");
        }

        private void RotationTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string rawValue } ||
                !TryParseInvariantInt(rawValue, out var rotation))
            {
                return;
            }

            SetVideoRotation(rotation);
            ResetInactivityTimer("click");
        }

        private void SleepTimerTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string rawValue })
            {
                return;
            }

            if (string.Equals(rawValue, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                CancelSleepTimer();
            }
            else if (TryParseInvariantInt(rawValue, out var minutes))
            {
                SetSleepTimer(TimeSpan.FromMinutes(minutes));
            }

            ResetInactivityTimer("click");
        }

        private void AdjustAudioDelay(double delta) => SetAudioDelay((_player?.AudioDelaySeconds ?? _playerPreferences.AudioDelaySeconds) + delta);
        private void SetAudioDelay(double value)
        {
            LogPlaybackState($"TOOL: audio delay value={value:0.###}");
            _player?.SetAudioDelaySeconds(value);
            if (_context != null)
            {
                _context.AudioDelaySeconds = _player?.AudioDelaySeconds ?? value;
            }

            RefreshToolToggleStates();
            RefreshInfoPanel();
            _ = SavePlayerPreferencesAsync();
            UpdatePlaybackHint();
        }

        private void AdjustSubtitleDelay(double delta) => SetSubtitleDelay((_player?.SubtitleDelaySeconds ?? _playerPreferences.SubtitleDelaySeconds) + delta);
        private void SetSubtitleDelay(double value)
        {
            LogPlaybackState($"TOOL: subtitle delay value={value:0.###}");
            _player?.SetSubtitleDelaySeconds(value);
            if (_context != null)
            {
                _context.SubtitleDelaySeconds = _player?.SubtitleDelaySeconds ?? value;
            }

            RefreshToolToggleStates();
            RefreshInfoPanel();
            _ = SavePlayerPreferencesAsync();
            UpdatePlaybackHint();
        }

        private void SetSubtitleScale(double value)
        {
            LogPlaybackState($"TOOL: subtitle scale value={value:0.###}");
            _player?.SetSubtitleScale(value);
            if (_context != null)
            {
                _context.SubtitleScale = _player?.SubtitleScale > 0 ? _player.SubtitleScale : value;
            }

            RefreshToolToggleStates();
            RefreshInfoPanel();
            _ = SavePlayerPreferencesAsync();
        }

        private void AdjustSubtitlePosition(int delta)
        {
            SetSubtitlePosition((_player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition) + delta);
        }

        private void SetSubtitlePosition(int position)
        {
            LogPlaybackState($"TOOL: subtitle position value={position}");
            _player?.SetSubtitlePosition(position);
            if (_context != null)
            {
                _context.SubtitlePosition = _player?.SubtitlePosition ?? position;
            }

            RefreshToolToggleStates();
            RefreshInfoPanel();
            _ = SavePlayerPreferencesAsync();
        }

        private void AdjustVideoZoom(double delta) => SetVideoZoom((_player?.VideoZoom ?? 0) + delta);
        private void SetVideoZoom(double value)
        {
            LogPlaybackState($"TOOL: video zoom value={value:0.###}");
            _player?.SetVideoZoom(value);
            RefreshToolToggleStates();
            RefreshInfoPanel();
        }

        private void SetVideoRotation(int value)
        {
            LogPlaybackState($"TOOL: video rotation value={value}");
            _player?.SetVideoRotation(value);
            RefreshToolToggleStates();
            RefreshInfoPanel();
        }

        private void SetSleepTimer(TimeSpan duration)
        {
            LogPlaybackState($"TOOL: sleep timer set seconds={duration.TotalSeconds:0}");
            _sleepDeadline = DateTimeOffset.UtcNow.Add(duration);
            _sleepTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sleepTimer.Tick -= SleepTimer_Tick;
            _sleepTimer.Tick += SleepTimer_Tick;
            _sleepTimer.Start();
            RefreshToolToggleStates();
            UpdatePlaybackHint();
        }

        private void CancelSleepTimer()
        {
            LogPlaybackState("TOOL: sleep timer cancelled");
            _sleepDeadline = null;
            _sleepTimer?.Stop();
            RefreshToolToggleStates();
            UpdatePlaybackHint();
        }

        private void SleepTimer_Tick(object? sender, object e)
        {
            if (!_sleepDeadline.HasValue)
            {
                _sleepTimer?.Stop();
                return;
            }

            if (DateTimeOffset.UtcNow >= _sleepDeadline.Value)
            {
                _sleepTimer?.Stop();
                _sleepDeadline = null;
                _player?.Stop();
                NavigateBack();
                return;
            }

            RefreshToolToggleStates();
            UpdatePlaybackHint();
        }

        private void CaptureScreenshot()
        {
            if (_player == null)
            {
                return;
            }

            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "Screenshots");
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, $"kroira-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            _player.CaptureScreenshot(filePath);
            ShowZapBanner("Screenshot saved", filePath);
        }

        private void SpeedItem_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null || sender is not ToggleMenuFlyoutItem { Tag: double speed })
            {
                return;
            }

            _player.SetPlaybackSpeed(speed);
            if (_context != null)
            {
                _context.InitialPlaybackSpeed = speed;
            }

            RefreshSpeedUi();
            RefreshInfoPanel();
            UpdatePlaybackHint();
            _ = SavePlayerPreferencesAsync();
        }

        private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_context == null || !_favoriteType.HasValue || _favoriteContentId <= 0)
            {
                return;
            }

            using var scope = ((App)Application.Current).Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var logicalCatalogStateService = scope.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
            _isFavorite = await logicalCatalogStateService.ToggleFavoriteAsync(db, _context.ProfileId, _favoriteType.Value, _favoriteContentId);
            UpdateFavoriteUi();
        }

        private void GuidePanelToggle_Click(object sender, RoutedEventArgs e) => TogglePanel(nameof(MiniGuidePanel));
        private void ChannelPanelToggle_Click(object sender, RoutedEventArgs e) => TogglePanel(nameof(ChannelSwitchPanel));
        private void EpisodePanelToggle_Click(object sender, RoutedEventArgs e) => TogglePanel(nameof(EpisodePanel));
        private void InfoPanelToggle_Click(object sender, RoutedEventArgs e) => TogglePanel(nameof(InfoPanel));

        private void ChannelSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyChannelSearchFilter();

        private async void ChannelSwitchList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PlayerChannelSwitchItem item)
            {
                await SwitchToChannelAsync(item.Id, "list");
            }
        }

        private async void EpisodeSwitchList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PlayerEpisodeSwitchItem item)
            {
                await SwitchToEpisodeAsync(item.Id);
            }
        }

        private async void GuideProgramAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.DataContext is not PlayerGuideProgramItem item)
            {
                return;
            }

            button.IsEnabled = false;
            try
            {
                await PlayGuideProgramAsync(item);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async void PreviousChannel_Click(object sender, RoutedEventArgs e) => await SwitchRelativeChannelAsync(-1);
        private async void NextChannel_Click(object sender, RoutedEventArgs e) => await SwitchRelativeChannelAsync(1);
        private async void LastChannel_Click(object sender, RoutedEventArgs e)
        {
            if (_lastChannelCandidateId > 0)
            {
                await SwitchToChannelAsync(_lastChannelCandidateId, "last_button");
            }
        }

        private void Back10_Click(object sender, RoutedEventArgs e) => TrySeekRelativeSeconds(-10);
        private void Back30_Click(object sender, RoutedEventArgs e) => TrySeekRelativeSeconds(-30);
        private void Forward10_Click(object sender, RoutedEventArgs e) => TrySeekRelativeSeconds(10);
        private void Forward30_Click(object sender, RoutedEventArgs e) => TrySeekRelativeSeconds(30);
        private void Restart_Click(object sender, RoutedEventArgs e) => RestartPlayerSession("restart", 0);
        private void RetryPlayback_Click(object sender, RoutedEventArgs e) => RetryCurrentPlayback();

        private void Page_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted)
            {
                return;
            }

            var delta = e.GetCurrentPoint(RootGrid).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            AdjustVolume(delta > 0 ? 5 : -5);
            ResetInactivityTimer("wheel");
            e.Handled = true;
        }
    }
}

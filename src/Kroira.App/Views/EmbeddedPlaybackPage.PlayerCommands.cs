#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Kroira.App.Views
{
    public sealed partial class EmbeddedPlaybackPage
    {
        private bool _isFavorite;

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
            _toggleToolItems.Clear();
            ToolsFlyout.Items.Clear();
            var isLive = IsLivePlayback();
            var canSeek = IsTimelineSeekAllowed();
            var hasEpisodeNavigation = _allEpisodeSwitchItems.Count > 1;
            var canUsePictureInPicture = CanUseFeature(EntitlementFeatureKeys.PlaybackPictureInPicture);

            if (isLive)
            {
                AddToolsItem("Mini guide", (_, _) => TogglePanel(nameof(MiniGuidePanel)));
                AddToolsItem("Channel list", (_, _) => TogglePanel(nameof(ChannelSwitchPanel)));
                if (_lastChannelCandidateId > 0)
                {
                    AddToolsItem("Last channel", async (_, _) => await SwitchToChannelAsync(_lastChannelCandidateId, "last_menu"));
                }

                if (canSeek)
                {
                    AddToolsItem("Jump to live", (_, _) => GoToLiveEdge("menu"));
                }
            }

            if (hasEpisodeNavigation)
            {
                AddToolsItem("Episode panel", (_, _) => TogglePanel(nameof(EpisodePanel)));
            }

            AddToolsItem("Info panel", (_, _) => TogglePanel(nameof(InfoPanel)));
            if (!isLive && canSeek)
            {
                AddToolsItem("Restart from beginning", (_, _) => RestartPlayerSession("restart", 0));
            }

            ToolsFlyout.Items.Add(new MenuFlyoutSeparator());
            AddToolsItem("Retry stream", (_, _) => RetryCurrentPlayback());
            AddToolsItem("Stop playback", (_, _) => NavigateBack());
            if (canUsePictureInPicture)
            {
                AddToolsItem(IsPictureInPictureMode() ? "Return from Picture in Picture" : "Picture in Picture", PictureInPicture_Click);
            }

            ToolsFlyout.Items.Add(new MenuFlyoutSeparator());
            AddToggleToolsItem("always_on_top", "Always on top", (_, _) => ToggleAlwaysOnTop());
            AddToggleToolsItem("deinterlace", "Deinterlace", (_, _) => ToggleDeinterlace());

            var displayMenu = new MenuFlyoutSubItem { Text = "Display" };
            AddAspectSubItem(displayMenu, "Automatic", PlaybackAspectMode.Automatic);
            AddAspectSubItem(displayMenu, "Fill window", PlaybackAspectMode.FillWindow);
            AddAspectSubItem(displayMenu, "16:9", PlaybackAspectMode.Ratio16x9);
            AddAspectSubItem(displayMenu, "4:3", PlaybackAspectMode.Ratio4x3);
            AddAspectSubItem(displayMenu, "1.85:1", PlaybackAspectMode.Ratio185x1);
            AddAspectSubItem(displayMenu, "2.35:1", PlaybackAspectMode.Ratio235x1);
            ToolsFlyout.Items.Add(displayMenu);

            var audioDelayMenu = new MenuFlyoutSubItem { Text = "Audio delay" };
            AddSubItem(audioDelayMenu, "-0.1s", (_, _) => AdjustAudioDelay(-0.1));
            AddSubItem(audioDelayMenu, "+0.1s", (_, _) => AdjustAudioDelay(0.1));
            AddSubItem(audioDelayMenu, "Reset", (_, _) => SetAudioDelay(0));
            ToolsFlyout.Items.Add(audioDelayMenu);

            var subtitleDelayMenu = new MenuFlyoutSubItem { Text = "Subtitle delay" };
            AddSubItem(subtitleDelayMenu, "-0.1s", (_, _) => AdjustSubtitleDelay(-0.1));
            AddSubItem(subtitleDelayMenu, "+0.1s", (_, _) => AdjustSubtitleDelay(0.1));
            AddSubItem(subtitleDelayMenu, "Reset", (_, _) => SetSubtitleDelay(0));
            ToolsFlyout.Items.Add(subtitleDelayMenu);

            var subtitleStyleMenu = new MenuFlyoutSubItem { Text = "Subtitle style" };
            foreach (var preset in _subtitleScalePresets)
            {
                AddSubItem(subtitleStyleMenu, preset.Label, (_, _) => SetSubtitleScale(preset.Scale));
            }
            AddSubItem(subtitleStyleMenu, "Raise", (_, _) => AdjustSubtitlePosition(-5));
            AddSubItem(subtitleStyleMenu, "Lower", (_, _) => AdjustSubtitlePosition(5));
            ToolsFlyout.Items.Add(subtitleStyleMenu);

            var zoomMenu = new MenuFlyoutSubItem { Text = "Zoom" };
            AddSubItem(zoomMenu, "Zoom in", (_, _) => AdjustVideoZoom(0.15));
            AddSubItem(zoomMenu, "Zoom out", (_, _) => AdjustVideoZoom(-0.15));
            AddSubItem(zoomMenu, "Reset zoom", (_, _) => SetVideoZoom(0));
            ToolsFlyout.Items.Add(zoomMenu);

            var rotationMenu = new MenuFlyoutSubItem { Text = "Rotation" };
            AddSubItem(rotationMenu, "0°", (_, _) => SetVideoRotation(0));
            AddSubItem(rotationMenu, "90°", (_, _) => SetVideoRotation(90));
            AddSubItem(rotationMenu, "180°", (_, _) => SetVideoRotation(180));
            AddSubItem(rotationMenu, "270°", (_, _) => SetVideoRotation(270));
            ToolsFlyout.Items.Add(rotationMenu);

            var sleepMenu = new MenuFlyoutSubItem { Text = "Sleep timer" };
            AddSubItem(sleepMenu, "30 minutes", (_, _) => SetSleepTimer(TimeSpan.FromMinutes(30)));
            AddSubItem(sleepMenu, "60 minutes", (_, _) => SetSleepTimer(TimeSpan.FromMinutes(60)));
            AddSubItem(sleepMenu, "90 minutes", (_, _) => SetSleepTimer(TimeSpan.FromMinutes(90)));
            AddSubItem(sleepMenu, "Cancel", (_, _) => CancelSleepTimer());
            ToolsFlyout.Items.Add(sleepMenu);

            ToolsFlyout.Items.Add(new MenuFlyoutSeparator());
            AddToolsItem("Take screenshot", (_, _) => CaptureScreenshot());
            RefreshToolToggleStates();
        }

        private void AddToolsItem(string text, RoutedEventHandler handler)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += handler;
            ToolsFlyout.Items.Add(item);
        }

        private void AddSubItem(MenuFlyoutSubItem parent, string text, RoutedEventHandler handler)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += handler;
            parent.Items.Add(item);
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

        private void AddToggleToolsItem(string key, string text, RoutedEventHandler handler)
        {
            var item = new ToggleMenuFlyoutItem { Text = text };
            item.Click += handler;
            ToolsFlyout.Items.Add(item);
            _toggleToolItems.Add((key, item));
        }

        private void RefreshToolToggleStates()
        {
            foreach (var toggle in _toggleToolItems)
            {
                toggle.Item.IsChecked = toggle.Key switch
                {
                    "always_on_top" => _windowManager?.IsAlwaysOnTop == true,
                    "deinterlace" => _player?.IsDeinterlaceEnabled ?? _playerPreferences.Deinterlace,
                    _ => false
                };
            }
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
            _guidePanelOpen = panelName == nameof(MiniGuidePanel) && !_guidePanelOpen;
            _channelPanelOpen = panelName == nameof(ChannelSwitchPanel) && !_channelPanelOpen;
            _episodePanelOpen = panelName == nameof(EpisodePanel) && !_episodePanelOpen;
            _infoPanelOpen = panelName == nameof(InfoPanel) && !_infoPanelOpen;
            UpdatePanelVisibility();
            FocusActivePanelTarget();
            ShowControls(persist: _guidePanelOpen || _channelPanelOpen || _episodePanelOpen || _infoPanelOpen, cause: "panel_toggle");
        }

        private void FocusActivePanelTarget()
        {
            if (_channelPanelOpen)
            {
                ChannelSearchBox.Focus(FocusState.Programmatic);
                return;
            }

            RootGrid.Focus(FocusState.Programmatic);
        }

        private bool CloseOpenPanels()
        {
            if (!_guidePanelOpen && !_channelPanelOpen && !_episodePanelOpen && !_infoPanelOpen)
            {
                return false;
            }

            _guidePanelOpen = false;
            _channelPanelOpen = false;
            _episodePanelOpen = false;
            _infoPanelOpen = false;
            UpdatePanelVisibility();
            RootGrid.Focus(FocusState.Programmatic);
            return true;
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

        private void RefreshInfoPanel()
        {
            var info = _player?.GetPlaybackInfoSnapshot() ?? new Services.Playback.MpvPlaybackInfoSnapshot();
            InfoSummaryText.Text = _stateMachine.State == PlaybackSessionState.Error
                ? _lastStateMessage
                : IsLivePlayback() ? "Live playback tools reflect real stream state and guide confidence." : "Seekable playback controls are active for this title.";
            InfoResolutionText.Text = info.HasVideo ? $"{info.Width}x{info.Height}" : "Unknown";
            InfoFpsText.Text = info.FramesPerSecond > 0 ? $"{info.FramesPerSecond:0.##}" : "Unknown";
            InfoVideoCodecText.Text = string.IsNullOrWhiteSpace(info.VideoCodec) ? "Unknown" : info.VideoCodec;
            InfoAudioCodecText.Text = string.IsNullOrWhiteSpace(info.AudioCodec) ? "Unknown" : info.AudioCodec;
            InfoSourceText.Text = string.IsNullOrWhiteSpace(_resolvedSourceName) ? "Unknown" : _resolvedSourceName;
            InfoSpeedText.Text = $"{(_player?.PlaybackSpeed > 0 ? _player.PlaybackSpeed : _playerPreferences.PlaybackSpeed):0.##}x";
            InfoSeekText.Text = IsTimelineSeekAllowed() ? "Seekable" : "Not seekable";
            InfoGuideText.Text = string.IsNullOrWhiteSpace(_resolvedGuideSummary) ? "No guide status" : _resolvedGuideSummary;
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
                BeginPlaybackAttempt(isRetry: false, retryReason: reason, startPositionMs: startPositionMs);
                return;
            }

            StopLoadTimeout();
            StopBufferTimeout();
            _progressPersistTimer?.Stop();
            DetachAndDisposePlayer();
            _player = CreatePlayer(_surface.Handle);
            _launchOverridesApplied = false;
            _lastPositionMs = Math.Max(0, startPositionMs);
            _lastDurationMs = 0;
            PositionText.Text = FormatTime(TimeSpan.FromMilliseconds(_lastPositionMs));
            DurationText.Text = "0:00";
            TimelineSlider.Value = 0;
            ResetTrackMenus();
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
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            var preferences = await browsePreferencesService.GetAsync(db, ProfileDomains.Live, _context.ProfileId);
            preferences.LastChannelId = channelId;
            preferences.RecentChannelIds.RemoveAll(id => id == channelId);
            preferences.RecentChannelIds.Insert(0, channelId);
            if (preferences.RecentChannelIds.Count > 10)
            {
                preferences.RecentChannelIds = preferences.RecentChannelIds.Take(10).ToList();
            }

            preferences.LiveChannelWatchCounts[channelId] =
                (preferences.LiveChannelWatchCounts.TryGetValue(channelId, out var currentCount) ? currentCount : 0) + 1;

            await browsePreferencesService.SaveAsync(db, ProfileDomains.Live, _context.ProfileId, preferences);
        }

        private async Task SwitchToChannelAsync(int channelId, string reason)
        {
            var nextItem = _allChannelSwitchItems.FirstOrDefault(item => item.Id == channelId);
            if (_context == null || nextItem == null || string.IsNullOrWhiteSpace(nextItem.StreamUrl))
            {
                return;
            }

            await RecordChannelLaunchAsync(channelId);
            _context.ContentId = nextItem.Id;
            _context.ContentType = PlaybackContentType.Channel;
            _context.StreamUrl = nextItem.StreamUrl;
            _context.StartPositionMs = 0;
            _context.RestoreAudioTrackSelection = false;
            _context.RestoreSubtitleTrackSelection = false;
            _context.PreferredAudioTrackId = string.Empty;
            _context.PreferredSubtitleTrackId = string.Empty;

            TitleText.Text = nextItem.Name;
            ShowZapBanner(nextItem.Name, nextItem.MetaText);
            await LoadEnhancedPlayerStateAsync(CurrentPlaybackSessionToken);
            RestartPlayerSession($"channel_switch:{reason}", 0);
        }

        private async Task SwitchToEpisodeAsync(int episodeId)
        {
            var nextItem = _allEpisodeSwitchItems.FirstOrDefault(item => item.Id == episodeId);
            if (_context == null || nextItem == null || string.IsNullOrWhiteSpace(nextItem.StreamUrl))
            {
                return;
            }

            _context.ContentId = nextItem.Id;
            _context.ContentType = PlaybackContentType.Episode;
            _context.StreamUrl = nextItem.StreamUrl;
            _context.StartPositionMs = nextItem.ResumePositionMs;
            TitleText.Text = nextItem.Title;
            await LoadEnhancedPlayerStateAsync(CurrentPlaybackSessionToken);
            RestartPlayerSession("episode_switch", nextItem.ResumePositionMs);
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

        private void ZapBannerTimer_Tick(object? sender, object e)
        {
            _zapBannerTimer?.Stop();
            ZapBanner.Visibility = Visibility.Collapsed;
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
            if (IsMenuOpen)
            {
                return true;
            }

            return e.Key != VirtualKey.Escape && IsTextInputFocused();
        }

        private bool HandleEscapeHotkey()
        {
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
            switch (e.Key)
            {
                case VirtualKey.Space:
                    TogglePlayPauseOrLive();
                    break;
                case VirtualKey.Left when IsTimelineSeekAllowed():
                    TrySeekRelativeSeconds(shiftDown ? -30 : -10);
                    break;
                case VirtualKey.Right when IsTimelineSeekAllowed():
                    TrySeekRelativeSeconds(shiftDown ? 30 : 10);
                    break;
                case VirtualKey.Up:
                    AdjustVolume(5);
                    break;
                case VirtualKey.Down:
                    AdjustVolume(-5);
                    break;
                case VirtualKey.PageUp when IsLivePlayback():
                    _ = SwitchRelativeChannelAsync(-1);
                    break;
                case VirtualKey.PageDown when IsLivePlayback():
                    _ = SwitchRelativeChannelAsync(1);
                    break;
                case VirtualKey.Back when IsLivePlayback() && _lastChannelCandidateId > 0:
                    _ = SwitchToChannelAsync(_lastChannelCandidateId, "last_hotkey");
                    break;
                case VirtualKey.F:
                    if (!IsPictureInPictureMode())
                    {
                        _windowManager?.ToggleFullscreen();
                    }
                    break;
                case VirtualKey.M:
                    Mute_Click(this, new RoutedEventArgs());
                    break;
                case VirtualKey.S:
                    ToggleSubtitleSelection();
                    break;
                case VirtualKey.A:
                    OpenTracksFlyout();
                    break;
                case VirtualKey.I:
                    TogglePanel(nameof(InfoPanel));
                    break;
                case VirtualKey.G when IsLivePlayback():
                    TogglePanel(nameof(MiniGuidePanel));
                    break;
                case VirtualKey.Escape:
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

            _player.SetDeinterlace(!_player.IsDeinterlaceEnabled);
            RefreshToolToggleStates();
            _ = SavePlayerPreferencesAsync();
        }

        private void AdjustAudioDelay(double delta) => SetAudioDelay((_player?.AudioDelaySeconds ?? _playerPreferences.AudioDelaySeconds) + delta);
        private void SetAudioDelay(double value)
        {
            _player?.SetAudioDelaySeconds(value);
            _ = SavePlayerPreferencesAsync();
            UpdatePlaybackHint();
        }

        private void AdjustSubtitleDelay(double delta) => SetSubtitleDelay((_player?.SubtitleDelaySeconds ?? _playerPreferences.SubtitleDelaySeconds) + delta);
        private void SetSubtitleDelay(double value)
        {
            _player?.SetSubtitleDelaySeconds(value);
            _ = SavePlayerPreferencesAsync();
            UpdatePlaybackHint();
        }

        private void SetSubtitleScale(double value)
        {
            _player?.SetSubtitleScale(value);
            _ = SavePlayerPreferencesAsync();
        }

        private void AdjustSubtitlePosition(int delta)
        {
            var position = (_player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition) + delta;
            _player?.SetSubtitlePosition(position);
            _ = SavePlayerPreferencesAsync();
        }

        private void AdjustVideoZoom(double delta) => SetVideoZoom((_player?.VideoZoom ?? 0) + delta);
        private void SetVideoZoom(double value) => _player?.SetVideoZoom(value);
        private void SetVideoRotation(int value) => _player?.SetVideoRotation(value);

        private void SetSleepTimer(TimeSpan duration)
        {
            _sleepDeadline = DateTimeOffset.UtcNow.Add(duration);
            _sleepTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sleepTimer.Tick -= SleepTimer_Tick;
            _sleepTimer.Tick += SleepTimer_Tick;
            _sleepTimer.Start();
            UpdatePlaybackHint();
        }

        private void CancelSleepTimer()
        {
            _sleepDeadline = null;
            _sleepTimer?.Stop();
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
            var existing = await db.Favorites.FirstOrDefaultAsync(favorite =>
                favorite.ProfileId == _context.ProfileId &&
                favorite.ContentType == _favoriteType.Value &&
                favorite.ContentId == _favoriteContentId);

            if (existing == null)
            {
                db.Favorites.Add(new Favorite
                {
                    ProfileId = _context.ProfileId,
                    ContentType = _favoriteType.Value,
                    ContentId = _favoriteContentId
                });
                _isFavorite = true;
            }
            else
            {
                db.Favorites.Remove(existing);
                _isFavorite = false;
            }

            await db.SaveChangesAsync();
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

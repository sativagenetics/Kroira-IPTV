#nullable enable
using System;
using Kroira.App.Models;
using Kroira.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Kroira.App.Services
{
    public interface IPictureInPictureService
    {
        bool IsActive { get; }
        event EventHandler? StateChanged;
        bool Enter(PlaybackLaunchContext context);
        bool RestoreToMainWindow(PlaybackLaunchContext context);
        void Close();
    }

    public sealed class PictureInPictureService : IPictureInPictureService
    {
        private PictureInPictureWindow? _window;
        private AppWindow? _appWindow;
        public bool IsActive => _window != null;
        public event EventHandler? StateChanged;

        public bool Enter(PlaybackLaunchContext context)
        {
            if (_window != null || context == null)
            {
                return false;
            }

            var window = new PictureInPictureWindow();
            window.Closed += Window_Closed;
            window.Activate();

            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            appWindow.Resize(new SizeInt32(420, 236));

            var launchContext = CloneForWindow(context, hwnd.ToInt64(), openInPictureInPicture: true);
            if (!window.NavigateToPlayback(launchContext))
            {
                window.Close();
                return false;
            }

            _window = window;
            _appWindow = appWindow;
            RaiseStateChanged();
            return true;
        }

        public bool RestoreToMainWindow(PlaybackLaunchContext context)
        {
            if (_window == null || context == null)
            {
                return false;
            }

            if (Application.Current is not App app || app.MainWindow is not MainWindow mainWindow)
            {
                return false;
            }

            var mainHwnd = WindowNative.GetWindowHandle(mainWindow);
            var launchContext = CloneForWindow(context, mainHwnd.ToInt64(), openInPictureInPicture: false);
            var windowManager = app.Services.GetRequiredService<IWindowManagerService>();
            if (windowManager.IsFullscreen)
            {
                windowManager.ExitFullscreen();
            }

            mainWindow.NavigateToPlayback(launchContext);
            mainWindow.Activate();

            _window.Close();
            CleanupWindow();
            return true;
        }

        public void Close()
        {
            if (_window == null)
            {
                return;
            }

            _window.Close();
            CleanupWindow();
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            CleanupWindow();
        }

        private void CleanupWindow()
        {
            if (_window != null)
            {
                _window.Closed -= Window_Closed;
            }

            _window = null;
            _appWindow = null;
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static PlaybackLaunchContext CloneForWindow(PlaybackLaunchContext context, long hostWindowHandle, bool openInPictureInPicture)
        {
            return new PlaybackLaunchContext
            {
                ProfileId = context.ProfileId,
                ContentId = context.ContentId,
                ContentType = context.ContentType,
                LogicalContentKey = context.LogicalContentKey,
                PreferredSourceProfileId = context.PreferredSourceProfileId,
                CatalogStreamUrl = context.CatalogStreamUrl,
                StreamUrl = context.StreamUrl,
                LiveStreamUrl = context.LiveStreamUrl,
                ProxyScope = context.ProxyScope,
                ProxyUrl = context.ProxyUrl,
                RoutingSummary = context.RoutingSummary,
                ProviderSummary = context.ProviderSummary,
                OperationalSummary = context.OperationalSummary,
                MirrorCandidateCount = context.MirrorCandidateCount,
                PlaybackMode = context.PlaybackMode,
                CatchupRequestKind = context.CatchupRequestKind,
                CatchupResolutionStatus = context.CatchupResolutionStatus,
                CatchupStatusText = context.CatchupStatusText,
                CatchupProgramTitle = context.CatchupProgramTitle,
                CatchupProgramStartTimeUtc = context.CatchupProgramStartTimeUtc,
                CatchupProgramEndTimeUtc = context.CatchupProgramEndTimeUtc,
                CatchupRequestedAtUtc = context.CatchupRequestedAtUtc,
                StartPositionMs = context.StartPositionMs,
                HostWindowHandle = hostWindowHandle,
                OpenInPictureInPicture = openInPictureInPicture,
                StartPaused = context.StartPaused,
                InitialVolume = context.InitialVolume,
                IsMuted = context.IsMuted,
                InitialAspectMode = context.InitialAspectMode,
                InitialPlaybackSpeed = context.InitialPlaybackSpeed,
                AudioDelaySeconds = context.AudioDelaySeconds,
                SubtitleDelaySeconds = context.SubtitleDelaySeconds,
                SubtitleScale = context.SubtitleScale,
                SubtitlePosition = context.SubtitlePosition,
                SubtitlesEnabled = context.SubtitlesEnabled,
                Deinterlace = context.Deinterlace,
                RestoreAudioTrackSelection = context.RestoreAudioTrackSelection,
                PreferredAudioTrackId = context.PreferredAudioTrackId,
                RestoreSubtitleTrackSelection = context.RestoreSubtitleTrackSelection,
                PreferredSubtitleTrackId = context.PreferredSubtitleTrackId
            };
        }
    }
}

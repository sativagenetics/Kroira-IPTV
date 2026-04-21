using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Microsoft.UI.Xaml;

namespace Kroira.App.Services
{
    public interface IWindowManagerService
    {
        bool IsFullscreen { get; }
        bool IsWindowActive { get; }
        bool IsAlwaysOnTop { get; }
        void Initialize(Window window);
        void ToggleFullscreen();
        void EnterFullscreen();
        void ExitFullscreen();
        void SetAlwaysOnTop(bool enabled);
        event EventHandler FullscreenStateChanged;
        event EventHandler WindowActivationChanged;
    }

    public class WindowManagerService : IWindowManagerService
    {
        private AppWindow _appWindow;
        private IntPtr _windowHandle;
        private bool _isWindowActive = true;
        private bool _isAlwaysOnTop;
        private bool _fullscreenTransitionInProgress;

        public bool IsFullscreen => _appWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        public bool IsWindowActive => _isWindowActive;
        public bool IsAlwaysOnTop => _isAlwaysOnTop;
        public event EventHandler FullscreenStateChanged;
        public event EventHandler WindowActivationChanged;

        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "startup-log.txt");

        public void Initialize(Window window)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _windowHandle = hWnd;
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Resize(new SizeInt32(1440, 920));
            window.Activated += Window_Activated;

            _appWindow.Changed += (s, e) =>
            {
                if (e.DidPresenterChange)
                {
                    _fullscreenTransitionInProgress = false;
                    Log($"WINDOW: presenter changed kind={_appWindow?.Presenter?.Kind}");
                    FullscreenStateChanged?.Invoke(this, EventArgs.Empty);
                }
            };
        }

        public void ToggleFullscreen()
        {
            if (IsFullscreen) ExitFullscreen();
            else EnterFullscreen();
        }

        public void EnterFullscreen()
        {
            SetPresenter(AppWindowPresenterKind.FullScreen, "enter");
        }

        public void ExitFullscreen()
        {
            SetPresenter(AppWindowPresenterKind.Default, "exit");
        }

        private void SetPresenter(AppWindowPresenterKind presenterKind, string reason)
        {
            if (_appWindow == null)
            {
                return;
            }

            if (_fullscreenTransitionInProgress)
            {
                Log($"WINDOW: presenter request skipped reason={reason} state=in_progress");
                return;
            }

            if (_appWindow.Presenter?.Kind == presenterKind)
            {
                Log($"WINDOW: presenter request skipped reason={reason} state=already_{presenterKind}");
                return;
            }

            try
            {
                _fullscreenTransitionInProgress = true;
                Log($"WINDOW: presenter request begin reason={reason} target={presenterKind}");
                _appWindow.SetPresenter(presenterKind);
            }
            catch (Exception ex)
            {
                _fullscreenTransitionInProgress = false;
                Log($"WINDOW: presenter request failed reason={reason} message={ex.Message}");
                throw;
            }
        }

        public void SetAlwaysOnTop(bool enabled)
        {
            if (_windowHandle == IntPtr.Zero)
            {
                _isAlwaysOnTop = enabled;
                return;
            }

            var insertAfter = enabled ? HwndTopMost : HwndNoTopMost;
            if (SetWindowPos(_windowHandle, insertAfter, 0, 0, 0, 0, SetWindowPosFlags.Nomove | SetWindowPosFlags.Nosize | SetWindowPosFlags.NoActivate))
            {
                _isAlwaysOnTop = enabled;
            }
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            var isActive = args.WindowActivationState != WindowActivationState.Deactivated;
            if (_isWindowActive == isActive)
            {
                return;
            }

            _isWindowActive = isActive;
            WindowActivationChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] WM {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private static readonly IntPtr HwndTopMost = new(-1);
        private static readonly IntPtr HwndNoTopMost = new(-2);

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            Nosize = 0x0001,
            Nomove = 0x0002,
            NoActivate = 0x0010
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            SetWindowPosFlags uFlags);
    }
}

using System;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Microsoft.UI.Xaml;

namespace Kroira.App.Services
{
    public interface IWindowManagerService
    {
        bool IsFullscreen { get; }
        bool IsWindowActive { get; }
        void Initialize(Window window);
        void ToggleFullscreen();
        void EnterFullscreen();
        void ExitFullscreen();
        event EventHandler FullscreenStateChanged;
        event EventHandler WindowActivationChanged;
    }

    public class WindowManagerService : IWindowManagerService
    {
        private AppWindow _appWindow;
        private bool _isWindowActive = true;

        public bool IsFullscreen => _appWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        public bool IsWindowActive => _isWindowActive;
        public event EventHandler FullscreenStateChanged;
        public event EventHandler WindowActivationChanged;

        public void Initialize(Window window)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Resize(new SizeInt32(1440, 920));
            window.Activated += Window_Activated;

            _appWindow.Changed += (s, e) =>
            {
                if (e.DidPresenterChange)
                {
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
            _appWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

        public void ExitFullscreen()
        {
            _appWindow?.SetPresenter(AppWindowPresenterKind.Default);
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
    }
}

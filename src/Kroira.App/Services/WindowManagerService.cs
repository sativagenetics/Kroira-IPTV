using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Kroira.App.Services
{
    public interface IWindowManagerService
    {
        bool IsFullscreen { get; }
        void Initialize(Window window);
        void ToggleFullscreen();
        void EnterFullscreen();
        void ExitFullscreen();
        event EventHandler FullscreenStateChanged;
    }

    public class WindowManagerService : IWindowManagerService
    {
        private AppWindow _appWindow;

        public bool IsFullscreen => _appWindow?.Presenter?.Kind == AppWindowPresenterKind.FullScreen;
        public event EventHandler FullscreenStateChanged;

        public void Initialize(Window window)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

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
    }
}

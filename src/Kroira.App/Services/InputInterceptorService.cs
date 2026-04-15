using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Runtime.InteropServices;

namespace Kroira.App.Services
{
    public interface IInputInterceptorService
    {
        void Initialize(Window window);
    }

    public class InputInterceptorService : IInputInterceptorService
    {
        private readonly IWindowManagerService _windowManager;
        private Window _window;
        private DispatcherTimer _pointerTimer;
        private bool _isPointerHidden = false;
        private bool _isWindowActive = true;

        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);

        public InputInterceptorService(IWindowManagerService windowManager)
        {
            _windowManager = windowManager;
            
            _pointerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _pointerTimer.Tick += PointerTimer_Tick;

            _windowManager.FullscreenStateChanged += (s, e) =>
            {
                if (!_windowManager.IsFullscreen)
                {
                    _pointerTimer.Stop();
                    ShowPointer();
                }
                else if (_isWindowActive)
                {
                    _pointerTimer.Start();
                }
            };
        }

        public void Initialize(Window window)
        {
            _window = window;
            _window.Activated += Window_Activated;

            if (_window.Content is UIElement rootElement)
            {
                // Attach global Keyboard Accelerators for decoupled bindings
                var f11 = new KeyboardAccelerator { Key = Windows.System.VirtualKey.F11 };
                f11.Invoked += (s, e) => { _windowManager.ToggleFullscreen(); e.Handled = true; };
                
                var esc = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
                esc.Invoked += (s, e) => 
                { 
                    if (_windowManager.IsFullscreen) 
                    {
                        _windowManager.ExitFullscreen(); 
                        e.Handled = true; 
                    }
                };

                rootElement.KeyboardAccelerators.Add(f11);
                rootElement.KeyboardAccelerators.Add(esc);

                // Attach Pointer Tracking globally
                rootElement.PointerMoved += RootElement_PointerMoved;
            }
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;

            if (!_isWindowActive)
            {
                _pointerTimer.Stop();
                ShowPointer();
            }
            else if (_windowManager.IsFullscreen)
            {
                _pointerTimer.Stop();
                _pointerTimer.Start();
            }
        }

        private void RootElement_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            ShowPointer();
            if (_windowManager.IsFullscreen && _isWindowActive)
            {
                _pointerTimer.Stop();
                _pointerTimer.Start();
            }
        }

        private void PointerTimer_Tick(object sender, object e)
        {
            HidePointer();
        }

        private void HidePointer()
        {
            if (!_isPointerHidden)
            {
                // Ensures strict OS counter boundary isn't leaked indefinitely
                while (ShowCursor(false) >= 0) { }
                _isPointerHidden = true;
            }
        }

        private void ShowPointer()
        {
            if (_isPointerHidden)
            {
                while (ShowCursor(true) < 0) { }
                _isPointerHidden = false;
            }
        }
    }
}

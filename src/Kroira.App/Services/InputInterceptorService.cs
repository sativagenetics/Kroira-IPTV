using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Kroira.App.Services
{
    public interface IInputInterceptorService
    {
        void Initialize(Window window);
    }

    public class InputInterceptorService : IInputInterceptorService
    {
        private readonly IWindowManagerService _windowManager;
        private readonly IEntitlementService _entitlementService;

        public InputInterceptorService(IWindowManagerService windowManager, IEntitlementService entitlementService)
        {
            _windowManager = windowManager;
            _entitlementService = entitlementService;
        }

        public void Initialize(Window window)
        {
            if (window.Content is UIElement rootElement)
            {
                // Suppress the visual accelerator key hint overlay (fixes stuck "F11" tooltip)
                rootElement.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

                // Attach global Keyboard Accelerators for decoupled bindings
                var f11 = new KeyboardAccelerator { Key = Windows.System.VirtualKey.F11 };
                f11.Invoked += (s, e) =>
                {
                    if (!_entitlementService.IsFeatureEnabled(EntitlementFeatureKeys.PlaybackFullscreen))
                    {
                        return;
                    }

                    _windowManager.ToggleFullscreen();
                    e.Handled = true;
                };

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
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Kroira.App.Services.Playback
{
    // Creates a Win32 child window parented to a WinUI 3 main window and keeps it
    // positioned/sized to match a XAML placeholder element. That child HWND is what
    // libmpv draws into via its --wid option.
    //
    // WinUI 3 renders via DesktopWindowXamlSource on top of the main HWND. Any real
    // child HWND of the main window is in "airspace" above composition, which means
    // it always paints on top of XAML in the rectangle it occupies. Playback control
    // bars therefore live *outside* this rectangle (top / bottom of the page) rather
    // than overlaying the video surface.
    internal sealed class VideoSurface : IDisposable
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;

        private const int GWLP_WNDPROC = -4;
        private const int IDC_ARROW = 32512;

        private const uint WM_SETCURSOR = 0x0020;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_MOUSEMOVE = 0x0200;

        private static readonly IntPtr HWND_TOP = IntPtr.Zero;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int width, int height, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private static readonly string WindowClassName = "KroiraMpvSurface";
        private static readonly IntPtr ArrowCursor = LoadCursor(IntPtr.Zero, (IntPtr)IDC_ARROW);
        private static readonly object ClassLock = new();
        private static bool _classRegistered;
        private static WndProcDelegate _classWndProc; // keep delegate alive for the lifetime of the class

        private readonly IntPtr _parentHwnd;
        private readonly FrameworkElement _host;
        private readonly Action _onDoubleClick;
        private readonly Action _onClick;
        private readonly Action<Point> _onMouseMoved;
        private readonly object _clickLock = new();

        private IntPtr _hwnd;
        private Timer _pendingClickTimer;
        private DateTime _suppressClickUntilUtc = DateTime.MinValue;
        private bool _disposed;
        private int _lastX = int.MinValue;
        private int _lastY = int.MinValue;
        private int _lastWidth = int.MinValue;
        private int _lastHeight = int.MinValue;

        public IntPtr Handle => _hwnd;

        public VideoSurface(IntPtr parentHwnd, FrameworkElement host,
            Action onClick, Action onDoubleClick, Action<Point> onMouseMoved)
        {
            _parentHwnd = parentHwnd;
            _host = host;
            _onClick = onClick;
            _onDoubleClick = onDoubleClick;
            _onMouseMoved = onMouseMoved;

            EnsureClassRegistered();
            Create();

            _host.SizeChanged += Host_SizeChanged;
            _host.LayoutUpdated += Host_LayoutUpdated;
            _host.Loaded += Host_Loaded;
        }

        private void EnsureClassRegistered()
        {
            lock (ClassLock)
            {
                if (_classRegistered) return;
                _classWndProc = StaticWndProc;
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    style = 0x0008 /* CS_DBLCLKS */,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_classWndProc),
                    hInstance = GetModuleHandle(null),
                    hCursor = ArrowCursor,
                    hbrBackground = (IntPtr)1, // COLOR_BACKGROUND
                    lpszClassName = WindowClassName,
                };
                if (RegisterClassEx(ref wc) == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    // Class may already be registered in a prior app session (unlikely here)
                    // but treat non-zero GetLastError as fatal.
                    if (err != 0 && err != 1410 /* ERROR_CLASS_ALREADY_EXISTS */)
                    {
                        throw new InvalidOperationException($"RegisterClassEx failed (err={err})");
                    }
                }
                _classRegistered = true;
            }
        }

        private void Create()
        {
            // Start with a 0-sized window in the top-left corner. UpdatePlacement will
            // move it to the host rectangle as soon as the host has a real size.
            _hwnd = CreateWindowEx(
                0,
                WindowClassName,
                "Kroira mpv surface",
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, 1, 1,
                _parentHwnd,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CreateWindowEx failed (err={err})");
            }

            // Per-instance WndProc: stash ourselves in GWLP_USERDATA so StaticWndProc can
            // forward input events. We use a thread-local map keyed by hwnd to avoid the
            // 32/64 SetWindowLongPtr split.
            _instances[_hwnd] = this;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, VideoSurface> _instances = new();

        private void Host_Loaded(object sender, RoutedEventArgs e) => UpdatePlacement(force: true);
        private void Host_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePlacement(force: true);
        private void Host_LayoutUpdated(object sender, object e) => UpdatePlacement();

        public void UpdatePlacement(bool force = false)
        {
            if (_disposed) return;
            if (_hwnd == IntPtr.Zero) return;
            if (_host.XamlRoot == null) return;
            if (_host.ActualWidth <= 0 || _host.ActualHeight <= 0)
            {
                SetWindowPos(_hwnd, HWND_TOP, 0, 0, 1, 1, SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
                return;
            }

            try
            {
                var transform = _host.TransformToVisual(null);
                var topLeft = transform.TransformPoint(new Point(0, 0));
                double scale = _host.XamlRoot.RasterizationScale;
                if (scale <= 0) scale = 1.0;

                int x = (int)Math.Round(topLeft.X * scale);
                int y = (int)Math.Round(topLeft.Y * scale);
                int w = (int)Math.Round(_host.ActualWidth * scale);
                int h = (int)Math.Round(_host.ActualHeight * scale);
                if (w < 1) w = 1;
                if (h < 1) h = 1;

                if (!force && x == _lastX && y == _lastY && w == _lastWidth && h == _lastHeight)
                {
                    return;
                }

                _lastX = x;
                _lastY = y;
                _lastWidth = w;
                _lastHeight = h;

                BringWindowToTop(_hwnd);
                SetWindowPos(_hwnd, HWND_TOP, x, y, w, h,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);
                InvalidateRect(_hwnd, IntPtr.Zero, false);
                UpdateWindow(_hwnd);
            }
            catch
            {
                // TransformToVisual can throw during teardown; ignore.
            }
        }

        private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (_instances.TryGetValue(hWnd, out var self))
            {
                if (self._disposed) return DefWindowProc(hWnd, msg, wParam, lParam);

                switch (msg)
                {
                    case WM_SETCURSOR:
                        SetCursor(ArrowCursor);
                        return (IntPtr)1;
                    case WM_LBUTTONDBLCLK:
                        self.HandleDoubleClick();
                        return IntPtr.Zero;
                    case WM_LBUTTONUP:
                        self.ScheduleSingleClick();
                        return IntPtr.Zero;
                    case WM_MOUSEMOVE:
                        self.HandleMouseMove(lParam);
                        return IntPtr.Zero;
                }
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void HandleMouseMove(IntPtr lParam)
        {
            if (_onMouseMoved == null)
            {
                return;
            }

            var point = new POINT
            {
                X = (short)(lParam.ToInt64() & 0xFFFF),
                Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF)
            };

            // Convert child-window client coordinates into screen coordinates so a
            // surface resize/reposition does not look like real mouse movement.
            if (ClientToScreen(_hwnd, ref point))
            {
                _onMouseMoved(new Point(point.X, point.Y));
                return;
            }

            _onMouseMoved(new Point(point.X, point.Y));
        }

        private void ScheduleSingleClick()
        {
            lock (_clickLock)
            {
                if (_disposed || DateTime.UtcNow < _suppressClickUntilUtc) return;
                _pendingClickTimer?.Dispose();
                _pendingClickTimer = new Timer(_ => FireSingleClick(), null, 240, Timeout.Infinite);
            }
        }

        private void FireSingleClick()
        {
            lock (_clickLock)
            {
                if (_disposed || DateTime.UtcNow < _suppressClickUntilUtc) return;
                _pendingClickTimer?.Dispose();
                _pendingClickTimer = null;
            }

            _onClick?.Invoke();
        }

        private void HandleDoubleClick()
        {
            lock (_clickLock)
            {
                if (_disposed) return;
                _suppressClickUntilUtc = DateTime.UtcNow.AddMilliseconds(360);
                _pendingClickTimer?.Dispose();
                _pendingClickTimer = null;
            }

            _onDoubleClick?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _host.Loaded -= Host_Loaded;
            _host.SizeChanged -= Host_SizeChanged;
            _host.LayoutUpdated -= Host_LayoutUpdated;
            lock (_clickLock)
            {
                _pendingClickTimer?.Dispose();
                _pendingClickTimer = null;
            }

            if (_hwnd != IntPtr.Zero)
            {
                var hwnd = _hwnd;
                _hwnd = IntPtr.Zero;
                _instances.TryRemove(hwnd, out _);
                SetWindowPos(hwnd, HWND_TOP, 0, 0, 1, 1, SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
                DestroyWindow(hwnd);
            }
        }
    }
}

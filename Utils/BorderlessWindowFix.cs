using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LbpArchiveToolkit.Utils
{
    public static class BorderlessWindowFix
    {
        public static void Apply(Window window)
        {
            HwndSource? hwndSource = null;
            HwndSourceHook? hook = null;

            hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == 0x0024) // WM_GETMINMAXINFO
                {
                    WmGetMinMaxInfo(hwnd, lParam, window);
                    handled = true;
                }
                return IntPtr.Zero;
            };

            window.SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                hwndSource = HwndSource.FromHwnd(handle);
                if (hook != null)
                {
                    hwndSource?.AddHook(hook);
                }
            };

            window.Closed += (s, e) =>
            {
                if (hook != null)
                {
                    hwndSource?.RemoveHook(hook);
                }
                hwndSource = null;
            };
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam, Window window)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
            int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                GetMonitorInfo(monitor, ref monitorInfo);

                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);

                var source = HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget != null)
                {
                    var matrix = source.CompositionTarget.TransformToDevice;
                    if (window.MinWidth > 0 && !double.IsNaN(window.MinWidth))
                        mmi.ptMinTrackSize.X = (int)(window.MinWidth * matrix.M11);
                    if (window.MinHeight > 0 && !double.IsNaN(window.MinHeight))
                        mmi.ptMinTrackSize.Y = (int)(window.MinHeight * matrix.M22);
                }

                Marshal.StructureToPtr(mmi, lParam, true);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    }
}
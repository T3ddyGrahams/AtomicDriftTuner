using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AtomicDriftTuner.Services;

/// <summary>Keep the custom window frame within the current monitor's usable area.</summary>
internal static class WindowBoundsService
{
    public static void Attach(Window window)
    {
        HwndSource? source = null;
        nint handle = nint.Zero;
        nint currentMonitor = nint.Zero;
        double minimumWidth = window.MinWidth, minimumHeight = window.MinHeight;
        void FitToMonitor()
        {
            if (source?.CompositionTarget is null || !TryMonitor(handle, out var monitor)) return;
            var transform = source.CompositionTarget.TransformFromDevice;
            var usable = transform.Transform(new Vector(monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top));
            window.MinWidth = Math.Min(minimumWidth, usable.X);
            window.MinHeight = Math.Min(minimumHeight, usable.Y);
            if (window.WindowState != WindowState.Normal) return;
            window.Width = Math.Min(window.Width, usable.X);
            window.Height = Math.Min(window.Height, usable.Y);
            var corner = transform.Transform(new Point(monitor.Work.Left, monitor.Work.Top));
            if (window.IsLoaded)
            {
                window.Left = Math.Clamp(window.Left, corner.X, corner.X + Math.Max(0, usable.X - window.Width));
                window.Top = Math.Clamp(window.Top, corner.Y, corner.Y + Math.Max(0, usable.Y - window.Height));
            }
        }
        HwndSourceHook hook = (nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message == 0x0024 && TryMonitor(hwnd, out var monitor)) // WM_GETMINMAXINFO
            {
                var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                bounds.MaxPosition = new PointI(monitor.Work.Left - monitor.Monitor.Left, monitor.Work.Top - monitor.Monitor.Top);
                bounds.MaxSize = new PointI(monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
                Marshal.StructureToPtr(bounds, lParam, false);
                // Let WPF also enforce its DPI-aware minimum tracking size.
            }
            else if (message == 0x02E0) // WM_DPICHANGED: let WPF apply the new DPI first.
                window.Dispatcher.BeginInvoke(new Action(FitToMonitor));
            return nint.Zero;
        };
        window.SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(window).Handle;
            currentMonitor = MonitorFromWindow(handle, 2);
            source = HwndSource.FromHwnd(handle);
            source?.AddHook(hook);
            FitToMonitor();
        };
        window.LocationChanged += (_, _) =>
        {
            if (handle == nint.Zero) return;
            var nextMonitor = MonitorFromWindow(handle, 2);
            if (nextMonitor == currentMonitor) return;
            currentMonitor = nextMonitor;
            window.Dispatcher.BeginInvoke(new Action(FitToMonitor));
        };
        window.Closed += (_, _) => source?.RemoveHook(hook);
    }

    private static bool TryMonitor(nint window, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(window, 2), ref info);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PointI(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectI Monitor, Work; public int Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public PointI Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}

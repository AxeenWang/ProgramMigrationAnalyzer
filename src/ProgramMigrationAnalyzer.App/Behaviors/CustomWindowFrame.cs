using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using ProgramMigrationAnalyzer.App.Controls;

namespace ProgramMigrationAnalyzer.App.Behaviors;

internal static class CustomWindowFrame
{
    internal static void Attach(Window window) => new FrameController(window).Attach();

    private sealed class FrameController(Window window)
    {
        private HwndSource? source;
        private bool maximizePressed;
        private bool trackingLeave;
        private WindowTitleBar? TitleBar => window.Template?.FindName("PART_TitleBar", window) as WindowTitleBar;
        private readonly WindowChrome chrome = new() { GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0) };

        internal void Attach()
        {
            window.StateChanged += OnStateChanged;
            window.Closed += OnClosed;
            UpdateChrome();
            WindowChrome.SetWindowChrome(window, chrome);
            // HwndSource hooks are last-in-first-out. Register after WindowChrome.
            window.SourceInitialized += OnSourceInitialized;
        }

        private void UpdateChrome()
        {
            var border = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip && window.WindowState == WindowState.Normal ? 6d : 0d;
            chrome.ResizeBorderThickness = new Thickness(border);
            chrome.CaptionHeight = (window.WindowState == WindowState.Maximized ? 42 : 43) - border;
        }

        private void OnStateChanged(object? sender, EventArgs e) => UpdateChrome();

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(WindowProcedure);
        }

        private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            var bar = TitleBar;
            if (bar is null) return IntPtr.Zero;
            switch (message)
            {
                case 0x0083 when IsZoomed(hwnd): // WM_NCCALCSIZE, constrain the custom client frame to this monitor's work area.
                    var proposed = Marshal.PtrToStructure<NativeRect>(lParam);
                    var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(MonitorFromRect(ref proposed, 2), ref monitor))
                    {
                        Marshal.StructureToPtr(monitor.Work, lParam, false);
                        handled = true;
                    }
                    break;
                case 0x0084: // WM_NCHITTEST, signed screen coordinates.
                    var point = ScreenPoint(lParam);
                    var local = window.PointFromScreen(point);
                    var border = chrome.ResizeBorderThickness;
                    if (local.X < border.Left || local.Y < border.Top || local.X >= window.ActualWidth - border.Right
                        || local.Y >= window.ActualHeight - border.Bottom) break;
                    if (bar.IsOverMaximize(point)) { handled = true; return new IntPtr(9); }
                    if (bar.IsOverIcon(point)) { handled = true; return new IntPtr(3); }
                    if (bar.IsOverCaption(point)) { handled = true; return new IntPtr(2); }
                    break;
                case 0x00A0: // WM_NCMOUSEMOVE, let Windows observe HTMAXBUTTON for Snap Layouts.
                    var hovered = wParam.ToInt64() == 9;
                    bar.ShowMaximizeInteraction(hovered, maximizePressed);
                    if (hovered && !trackingLeave)
                    {
                        var track = new TrackMouse { Size = (uint)Marshal.SizeOf<TrackMouse>(), Flags = 0x12, Handle = hwnd };
                        trackingLeave = TrackMouseEvent(ref track);
                    }
                    break;
                case 0x02A2: // WM_NCMOUSELEAVE
                    trackingLeave = false;
                    bar.ShowMaximizeInteraction(false, maximizePressed);
                    break;
                case 0x00A1 when wParam.ToInt64() == 9: // WM_NCLBUTTONDOWN
                    if (bar.IsOverMaximize(ScreenPoint(lParam)))
                    {
                        maximizePressed = true;
                        SetCapture(hwnd);
                        bar.ShowMaximizeInteraction(true, true);
                        handled = true;
                    }
                    break;
                case 0x00A2 when maximizePressed: // WM_NCLBUTTONUP
                    FinishMaximize(hwnd, bar, ScreenPoint(lParam));
                    handled = true;
                    break;
                case 0x0202 when maximizePressed: // WM_LBUTTONUP while captured.
                    var client = new NativePoint { X = unchecked((short)lParam.ToInt64()), Y = unchecked((short)(lParam.ToInt64() >> 16)) };
                    if (ClientToScreen(hwnd, ref client)) FinishMaximize(hwnd, bar, new Point(client.X, client.Y));
                    else CancelPress(hwnd, bar);
                    handled = true;
                    break;
                case 0x0200 when maximizePressed: // WM_MOUSEMOVE while captured.
                    var cursor = new NativePoint();
                    if (GetCursorPos(ref cursor)) bar.ShowMaximizeInteraction(bar.IsOverMaximize(new Point(cursor.X, cursor.Y)), true);
                    break;
                case 0x0215: // WM_CAPTURECHANGED
                case 0x001F: // WM_CANCELMODE
                    CancelPress(hwnd, bar);
                    break;
            }
            return IntPtr.Zero;
        }

        private void FinishMaximize(IntPtr hwnd, WindowTitleBar bar, Point screenPoint)
        {
            var activate = bar.IsOverMaximize(screenPoint);
            CancelPress(hwnd, bar);
            if (activate) bar.ToggleMaximize();
        }

        private void CancelPress(IntPtr hwnd, WindowTitleBar bar)
        {
            maximizePressed = false;
            if (GetCapture() == hwnd) ReleaseCapture();
            bar.ShowMaximizeInteraction(false, false);
        }

        private static Point ScreenPoint(IntPtr value) => new(unchecked((short)value.ToInt64()), unchecked((short)(value.ToInt64() >> 16)));

        private void OnClosed(object? sender, EventArgs e)
        {
            if (source is not null)
            {
                if (TitleBar is WindowTitleBar bar) CancelPress(source.Handle, bar);
                source.RemoveHook(WindowProcedure);
                source = null;
            }
            window.SourceInitialized -= OnSourceInitialized;
            window.StateChanged -= OnStateChanged;
            window.Closed -= OnClosed;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public int Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouse { public uint Size; public uint Flags; public IntPtr Handle; public uint HoverTime; }

    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TrackMouseEvent(ref TrackMouse track);
}

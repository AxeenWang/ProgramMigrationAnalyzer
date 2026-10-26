using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace ProgramMigrationAnalyzer.WpfChecks;

internal static class WindowChromeChecks
{
    internal static void AssertCaption(Window window)
    {
        if (WindowChrome.GetWindowChrome(window) is null ||
            window.Template.FindName("PART_TitleBar", window) is not FrameworkElement titleBar || !titleBar.IsVisible)
            throw new InvalidOperationException("Both windows must present the shared custom title bar with WindowChrome.");
        var text = (TextBlock)titleBar.FindName("CaptionText");
        Check(window.Icon is not null && titleBar.FindName("ApplicationMark") is Image { Source: not null },
            "Both the Windows icon and custom caption must use the application's original icon.");
        Check(text.Text == window.Title && text.Text.EndsWith("(1.0.1.0)", StringComparison.Ordinal),
            "The shared title must show the current window title and assembly version.");
        var resizable = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        Check(Button(window, "MinimizeButton").IsVisible == resizable && Button(window, "MaximizeButton").IsVisible == resizable
              && Button(window, "CloseButton").IsVisible, "Caption controls do not match the window's resize policy.");
        var handle = new WindowInteropHelper(window).Handle;
        var caption = text.PointToScreen(new Point(15, text.ActualHeight / 2));
        Check(Hit(handle, caption) == 2, "The title must remain a native draggable caption.");
        var iconPoint = Center((FrameworkElement)titleBar.FindName("IconRegion"));
        var iconHit = Hit(handle, iconPoint);
        Check(iconHit == 3, $"The application icon must retain the native system menu hit region (hit={iconHit}, custom region={((ProgramMigrationAnalyzer.App.Controls.WindowTitleBar)titleBar).IsOverIcon(iconPoint)}).");
        if (resizable)
        {
            Check(Hit(handle, Center(Button(window, "MaximizeButton"))) == 9,
                "The custom maximize button must expose HTMAXBUTTON for Windows 11 Snap Layouts.");
            Check(Hit(handle, window.PointToScreen(new Point(2, window.ActualHeight / 2))) == 10,
                "The main window must retain its native left resize border.");
        }
        else Check(Hit(handle, window.PointToScreen(new Point(2, window.ActualHeight / 2))) == 1,
            "The fixed-size login window must not expose a resize border.");
    }

    internal static Button Button(Window window, string name) =>
        (Button)((FrameworkElement)window.Template.FindName("PART_TitleBar", window)).FindName(name);

    internal static async Task CheckMainAsync(Window window)
    {
        AssertCaption(window);
        var titleBar = (ProgramMigrationAnalyzer.App.Controls.WindowTitleBar)window.Template.FindName("PART_TitleBar", window);
        titleBar.ShowMaximizeInteraction(true, true);
        var pressedColor = ((SolidColorBrush)Button(window, "MaximizeButton").Background).Color;
        titleBar.ShowMaximizeInteraction(false, true);
        Check(((SolidColorBrush)Button(window, "MaximizeButton").Background).Color == Colors.Transparent,
            "Dragging a held maximize press outside must clear its pressed visual without activating the action.");
        titleBar.ShowMaximizeInteraction(true, true);
        Check(((SolidColorBrush)Button(window, "MaximizeButton").Background).Color == pressedColor,
            "Moving a held maximize press back inside must restore its pressed visual.");
        titleBar.ShowMaximizeInteraction(false, false);
        var handle = new WindowInteropHelper(window).Handle;
        var originalLeft = window.Left;
        var originalTop = window.Top;
        try
        {
            window.Left = -window.ActualWidth - 200;
            window.Top = -100;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var negative = Center(Button(window, "MaximizeButton"));
            Check(negative.X < 0 && negative.Y < 0 && Hit(handle, negative) == 9,
                "Native maximize hit testing lost signed coordinates on negative screen positions.");
        }
        finally { window.Left = originalLeft; window.Top = originalTop; }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        // Exercise actual native messages so a broken hook order cannot pass through WPF Click alone.
        var maximize = Center(Button(window, "MaximizeButton"));
        SendMessage(handle, 0x00A1, new IntPtr(9), Pack(maximize));
        Check(GetCapture() == handle, "A native maximize press must capture the mouse until release.");
        SendMessage(handle, 0x00A2, new IntPtr(9), Pack(maximize));
        await WaitForState(window, WindowState.Maximized);
        Check(GetCapture() != handle && Equals(Button(window, "MaximizeButton").ToolTip, "還原"),
            "Maximization must release capture and update the restore action.");
        CheckMaximizedWorkArea(handle);
        var maximizedCaption = ((FrameworkElement)window.Template.FindName("PART_TitleBar", window)).PointToScreen(new Point(200, 39));
        Check(Hit(handle, maximizedCaption) == 2, "The full visible caption must remain draggable after maximizing.");
        Click(Button(window, "MaximizeButton"));
        await WaitForState(window, WindowState.Normal);

        maximize = Center(Button(window, "MaximizeButton"));
        var released = new NativePoint { X = (int)maximize.X, Y = (int)maximize.Y };
        ScreenToClient(handle, ref released);
        SendMessage(handle, 0x00A1, new IntPtr(9), Pack(maximize));
        SendMessage(handle, 0x0202, IntPtr.Zero, Pack(new Point(released.X, released.Y)));
        await WaitForState(window, WindowState.Maximized);
        Click(Button(window, "MaximizeButton"));
        await WaitForState(window, WindowState.Normal);

        maximize = Center(Button(window, "MaximizeButton"));
        SendMessage(handle, 0x00A1, new IntPtr(9), Pack(maximize));
        SendMessage(handle, 0x0215, IntPtr.Zero, IntPtr.Zero);
        Check(GetCapture() != handle && window.WindowState == WindowState.Normal,
            "Canceled native capture must not maximize the window or retain capture.");
        SendMessage(handle, 0x00A1, new IntPtr(9), Pack(maximize));
        var outside = new NativePoint { X = (int)window.PointToScreen(new Point(150, 20)).X, Y = (int)window.PointToScreen(new Point(150, 20)).Y };
        ScreenToClient(handle, ref outside);
        SendMessage(handle, 0x0202, IntPtr.Zero, Pack(new Point(outside.X, outside.Y)));
        Check(window.WindowState == WindowState.Normal && GetCapture() != handle,
            "Releasing outside maximize must cancel the operation.");

        Click(Button(window, "MinimizeButton"));
        await WaitForState(window, WindowState.Minimized);
        SystemCommands.RestoreWindow(window);
        await WaitForState(window, WindowState.Normal);
        var caption = ((FrameworkElement)window.Template.FindName("PART_TitleBar", window)).PointToScreen(new Point(200, 20));
        SendMessage(handle, 0x00A3, new IntPtr(2), Pack(caption));
        await WaitForState(window, WindowState.Maximized);
        caption = ((FrameworkElement)window.Template.FindName("PART_TitleBar", window)).PointToScreen(new Point(200, 20));
        SendMessage(handle, 0x00A3, new IntPtr(2), Pack(caption));
        await WaitForState(window, WindowState.Normal);
        Console.WriteLine("PASS custom frame: native caption/system menu/resize hit tests, Snap maximize hit, signed coordinates, capture cancel, minimize, maximize/restore, taskbar work area and double-click.");
    }

    internal static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static async Task WaitForState(Window window, WindowState state)
    {
        await EmbeddedLoginUiChecks.WaitUntilAsync(() => window.WindowState == state);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static Point Center(FrameworkElement element) => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
    private static long Hit(IntPtr handle, Point point) => SendMessage(handle, 0x0084, IntPtr.Zero, Pack(point)).ToInt64();
    private static IntPtr Pack(Point point) => new(unchecked(((int)Math.Round(point.Y) << 16) | ((int)Math.Round(point.X) & 0xffff)));
    private static void Click(Button button) => EmbeddedLoginUiChecks.Click(button);

    private static void CheckMaximizedWorkArea(IntPtr handle)
    {
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Check(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor), "Cannot inspect the maximized monitor work area.");
        Check(GetClientRect(handle, out var client), "Cannot inspect maximized client bounds.");
        var topLeft = new NativePoint();
        var bottomRight = new NativePoint { X = client.Right, Y = client.Bottom };
        Check(ClientToScreen(handle, ref topLeft) && ClientToScreen(handle, ref bottomRight), "Cannot convert maximized client bounds.");
        Check(topLeft.X >= monitor.Work.Left - 2 && topLeft.Y >= monitor.Work.Top - 2
              && bottomRight.X <= monitor.Work.Right + 2 && bottomRight.Y <= monitor.Work.Bottom + 2,
            $"The maximized custom frame must stay within the monitor work area and not cover the taskbar. client=({topLeft.X},{topLeft.Y})-({bottomRight.X},{bottomRight.Y}), work=({monitor.Work.Left},{monitor.Work.Top})-({monitor.Work.Right},{monitor.Work.Bottom}).");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public int Flags; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(IntPtr handle, ref NativePoint point);
}

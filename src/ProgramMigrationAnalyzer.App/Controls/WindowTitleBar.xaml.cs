using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProgramMigrationAnalyzer.App.Controls;

public partial class WindowTitleBar : UserControl
{
    public WindowTitleBar() => InitializeComponent();

    internal bool IsOverMaximize(Point screenPoint)
    {
        var owner = Window.GetWindow(this);
        return owner?.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip && Contains(MaximizeButton, screenPoint);
    }

    internal bool IsOverIcon(Point screenPoint) => Contains(IconRegion, screenPoint);

    internal bool IsOverCaption(Point screenPoint) => Contains(this, screenPoint)
        && !Contains(MinimizeButton, screenPoint) && !Contains(MaximizeButton, screenPoint) && !Contains(CloseButton, screenPoint);

    private static bool Contains(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || !element.IsEnabled) return false;
        return new Rect(new Size(element.ActualWidth, element.ActualHeight)).Contains(element.PointFromScreen(screenPoint));
    }

    internal void ShowMaximizeInteraction(bool hovered, bool pressed) =>
        MaximizeButton.SetCurrentValue(BackgroundProperty, pressed && hovered ? PressedBrush : hovered ? HoverBrush : Brushes.Transparent);

    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromRgb(0x31, 0x54, 0x75));
    private static readonly Brush PressedBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x68, 0x87));

    internal void ToggleMaximize()
    {
        var window = Window.GetWindow(this);
        if (window?.ResizeMode is not (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)) return;
        if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
        else SystemCommands.MaximizeWindow(window);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window window && window.ResizeMode != ResizeMode.NoResize) SystemCommands.MinimizeWindow(window);
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window window) SystemCommands.CloseWindow(window);
    }
}

using System.Windows;
using ICSharpCode.AvalonEdit;

namespace ProgramMigrationAnalyzer.App.Behaviors;

public static class AvalonEditTextBinding
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(AvalonEditTextBinding),
        new FrameworkPropertyMetadata(string.Empty, OnTextChanged));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not TextEditor editor)
        {
            return;
        }

        var text = eventArgs.NewValue as string ?? string.Empty;
        if (!string.Equals(editor.Text, text, StringComparison.Ordinal))
        {
            editor.Text = text;
            editor.ScrollToHome();
        }
    }
}

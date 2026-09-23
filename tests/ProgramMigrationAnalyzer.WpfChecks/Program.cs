using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Infrastructure;
using ProgramMigrationAnalyzer.Parsers;
using ProgramMigrationAnalyzer.Translation;

namespace ProgramMigrationAnalyzer.WpfChecks;

internal static class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static readonly string Scratch = Path.Combine(Root, ".codex-tmp", "2026-09-23_phase3-wpf", Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main()
    {
        if (!File.Exists(Path.Combine(Root, "ProgramMigrationAnalyzer.sln")))
        {
            throw new InvalidOperationException("WPF checks must run from the ProgramMigrationAnalyzer project.");
        }

        Directory.CreateDirectory(Scratch);
        var app = new ProgramMigrationAnalyzer.App.App();
        app.InitializeComponent();
        var dialogs = new ScriptedDialogs();
        var viewModel = new MainViewModel(
            new SourceFileService(),
            [new CSharpSourceParser(), new Informix4GlParser()],
            new MigrationAnalyzer(),
            new MarkdownReportGenerator(),
            new MigrationSourceTranslator(),
            new OutputWriter(Path.Combine(Scratch, "output")),
            dialogs);
        var window = new MainWindow(viewModel);
        app.MainWindow = window;
        var exitCode = 0;
        app.DispatcherUnhandledException += (_, eventArgs) =>
        {
            Console.Error.WriteLine(eventArgs.Exception);
            exitCode = 1;
            eventArgs.Handled = true;
            window.Close();
        };

        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                await CheckUiAsync(window, viewModel, dialogs);
                if (Environment.GetEnvironmentVariable("PMA_WPF_CAPTURE") == "1")
                {
                    CaptureWindow(window);
                }

                Console.WriteLine("Phase 3 WPF checks passed: opening files/folder, source and analysis bindings, Markdown preview, .NET 8/10 translation, diff, save, status and log.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                exitCode = 1;
            }
            finally
            {
                window.Close();
            }
        }));

        try
        {
            app.Run(window);
            return exitCode;
        }
        finally
        {
            Directory.Delete(Scratch, recursive: true);
        }
    }

    private static async Task CheckUiAsync(MainWindow window, MainViewModel viewModel, ScriptedDialogs dialogs)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var tabs = FindVisual<TabControl>(window).Single();
        Check(tabs.Items.Count == 5, "The main window does not contain all five preview tabs.");
        Check(FindButton(window, "開啟檔案").Command is IAsyncRelayCommand, "Open file button is not bound.");
        Check(FindButton(window, "開啟資料夾").Command is IAsyncRelayCommand, "Open folder button is not bound.");
        Check(FindButton(window, "開始分析").Command is IAsyncRelayCommand, "Analyze button is not bound.");
        Check(FindButton(window, "開始轉譯").Command is IAsyncRelayCommand, "Translate button is not bound.");

        var source4Gl = Path.Combine(Root, "samples", "4gl", "CustomerQuery.4gl");
        dialogs.Files.Enqueue(source4Gl);
        await ClickAsync(window, "開啟檔案");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(viewModel.SelectedDocument?.LanguageName == "4GL", "4GL language was not shown.");
        Check(FindVisual<ListBox>(window).Any(list => ReferenceEquals(list.ItemsSource, viewModel.Documents) && list.Items.Count == 1),
            "The file list did not show the opened document.");
        var sourceEditor = FindVisual<TextEditor>(tabs).Single();
        await Task.Delay(150);
        Check(sourceEditor.Text.Contains("MAIN", StringComparison.Ordinal),
            $"The source editor did not display the 4GL source (editor length {sourceEditor.Text.Length}, model length {viewModel.SelectedDocument?.SourceText.Length}).");

        await ClickAsync(window, "開始分析");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var document = viewModel.SelectedDocument!;
        var analysisView = document.AnalysisView;
        Check(analysisView is not null && analysisView.Functions.Count > 0 &&
            analysisView.SqlStatements.Count > 0,
            $"Scenario A analysis is missing functions or SQL. Status: {viewModel.StatusMessage}. Last log: {viewModel.Logs.LastOrDefault()}. Last dialog: {dialogs.Errors.LastOrDefault()}.");
        Check(document.Markdown.Contains("CUSTOMER", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(document.AnalysisOutputPath), "Scenario A Markdown report was not generated.");
        SelectTab(tabs, "分析結果");
        Check(FindVisual<ItemsControl>(tabs).Any(item => ReferenceEquals(item.ItemsSource, analysisView?.Functions)),
            "The analysis tab did not bind the function list.");

        SelectTab(tabs, "Markdown");
        await CheckMarkdownPreviewAsync(tabs, viewModel, dialogs);

        var targetPicker = FindVisual<ComboBox>(window).Single(box => ReferenceEquals(box.ItemsSource, viewModel.TargetOptions));
        targetPicker.SelectedItem = viewModel.TargetOptions.Single(option => option.Name == ".NET 8");
        await ClickAsync(window, "開始轉譯");
        Check(document.TranslationOutputPath.EndsWith(".net8.cs", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(document.TranslationOutputPath), ".NET 8 translation was not written.");
        targetPicker.SelectedItem = viewModel.TargetOptions.Single(option => option.Name == ".NET 10");
        await ClickAsync(window, "開始轉譯");
        Check(document.TranslationOutputPath.EndsWith(".net10.cs", StringComparison.OrdinalIgnoreCase) &&
            document.TranslationCode.Contains("class", StringComparison.Ordinal) &&
            File.Exists(document.TranslationOutputPath), "Scenario C translation was not written.");
        SelectTab(tabs, "轉譯程式");
        Check(FindVisual<TextEditor>(tabs).Single().Text == document.TranslationCode,
            "The translated code editor did not update.");
        SelectTab(tabs, "Diff");
        Check(FindVisual<TextEditor>(tabs).Count() == 2 &&
            FindVisual<TextEditor>(tabs).Any(editor => editor.Text == document.TranslationCode),
            "The Diff tab did not show the translation.");

        var savedReport = Path.Combine(Scratch, "saved.md");
        dialogs.SavePath = savedReport;
        await ClickAsync(window, "儲存報告");
        Check(File.ReadAllText(savedReport) == document.Markdown, "Save report did not write the displayed Markdown.");

        dialogs.Folders.Enqueue(Path.Combine(Root, "samples", "4gl"));
        await ClickAsync(window, "開啟資料夾");
        Check(viewModel.Documents.Count >= 5, "Opening the 4GL folder did not populate the file list.");

        dialogs.Files.Enqueue(Path.Combine(Root, "samples", "csharp-framework", "LegacyApiClient.cs"));
        await ClickAsync(window, "開啟檔案");
        await ClickAsync(window, "開始分析");
        var legacy = viewModel.SelectedDocument!;
        Check(legacy.LanguageName == "C#" && legacy.AnalysisView is not null &&
            legacy.AnalysisView.MigrationIssues.Any(issue => issue.Description.Contains("WebRequest", StringComparison.OrdinalIgnoreCase) ||
                issue.Title.Contains("WebRequest", StringComparison.OrdinalIgnoreCase)) &&
            legacy.AnalysisView.Dependencies.Any(item => item.Name.Contains("XML", StringComparison.OrdinalIgnoreCase)) &&
            File.Exists(legacy.AnalysisOutputPath), "Scenario B analysis did not show the expected C# issues.");
        Check(!viewModel.IsBusy && viewModel.StatusMessage.Contains("完成", StringComparison.Ordinal) &&
            viewModel.Logs.Any(item => item.Contains("Completed", StringComparison.Ordinal)),
            "The status or log did not show completion.");
        Check(dialogs.Errors.Count == 0 || dialogs.Errors.All(item => item.Title == "Markdown 預覽無法啟動"),
            "The UI displayed an unexpected error dialog.");
    }

    private static async Task CheckMarkdownPreviewAsync(TabControl tabs, MainViewModel viewModel, ScriptedDialogs dialogs)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var fallback = FindVisual<TextBox>(tabs).FirstOrDefault();
            if (fallback?.Visibility == Visibility.Visible)
            {
                Check(fallback.Text == viewModel.SelectedDocument?.Markdown &&
                    viewModel.Logs.Any(item => item.Contains("WebView2 Markdown preview unavailable", StringComparison.Ordinal)) &&
                    dialogs.Errors.Any(item => item.Title == "Markdown 預覽無法啟動"),
                    "WebView2 fallback did not display Markdown and record the failure.");
                Console.WriteLine($"WebView2 fallback: {dialogs.Errors.Last().Message}");
                return;
            }

            var browser = FindVisual<WebView2>(tabs).FirstOrDefault();
            if (browser?.CoreWebView2 is not null)
            {
                var body = await browser.ExecuteScriptAsync("document.body.innerText");
                if (body.Contains("CustomerQuery", StringComparison.OrdinalIgnoreCase))
                {
                    if (Environment.GetEnvironmentVariable("PMA_WPF_CAPTURE") == "1")
                    {
                        var path = Path.Combine(Root, ".codex-tmp", "2026-09-23_phase3-visual", "markdown.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await using var stream = File.Create(path);
                        await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                        Console.WriteLine($"Markdown capture: {path}");
                    }

                    return;
                }
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("Markdown preview neither rendered the report nor entered its fallback state.");
    }

    private static void SelectTab(TabControl tabs, string title)
    {
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, title));
        tabs.UpdateLayout();
    }

    private static async Task ClickAsync(Window window, string title)
    {
        var command = FindButton(window, title).Command as IAsyncRelayCommand;
        if (command is null || !command.CanExecute(null))
        {
            throw new InvalidOperationException($"The {title} button is unavailable.");
        }

        await command.ExecuteAsync(null);
    }

    private static Button FindButton(Window window, string title) =>
        FindVisual<Button>(window).Single(button => Equals(button.Content, title));

    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisual<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void CaptureWindow(Window window)
    {
        SelectTab(FindVisual<TabControl>(window).Single(), "分析結果");
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(Root, ".codex-tmp", "2026-09-23_phase3-visual", "window.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"WPF capture: {path}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ScriptedDialogs : IFileDialogService
    {
        public Queue<string> Files { get; } = new();
        public Queue<string> Folders { get; } = new();
        public List<(string Title, string Message)> Errors { get; } = [];
        public string? SavePath { get; set; }

        public string? SelectSourceFile() => Files.Dequeue();
        public string? SelectSourceFolder() => Folders.Dequeue();
        public string? SelectMarkdownSavePath(string suggestedFileName) => SavePath;
        public void ShowError(string title, string message) => Errors.Add((title, message));
    }
}

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
using Markdig;

namespace ProgramMigrationAnalyzer.WpfChecks;

internal static class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static readonly string Scratch = Path.Combine(Root, ".camp_codex", "2026-10-26_remove-authentication", "wpf", Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main()
    {
        if (!File.Exists(Path.Combine(Root, "ProgramMigrationAnalyzer.sln")))
        {
            throw new InvalidOperationException("WPF checks must run from the ProgramMigrationAnalyzer project.");
        }

        Directory.CreateDirectory(Scratch);
        CheckFourGlMarkdownRenderingAsync().GetAwaiter().GetResult();
        CheckPreviewSessionLifetime();
        var dialogs = new ScriptedDialogs();
        var app = new TestApp(dialogs);
        app.Resources = (ResourceDictionary)Application.LoadComponent(
            new Uri("/ProgramMigrationAnalyzer.App;component/Resources/ApplicationResources.xaml", UriKind.Relative));
        var exitCode = 0;
        app.DispatcherUnhandledException += (_, eventArgs) =>
        {
            Console.Error.WriteLine(eventArgs.Exception);
            exitCode = 1;
            eventArgs.Handled = true;
            app.Shutdown();
        };

        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                var window = app.Windows.OfType<MainWindow>().Single();
                var viewModel = (MainViewModel)window.DataContext;
                Check(app.Windows.Count == 1 && app.MainCalls == 1
                    && viewModel.OpenFileCommand.CanExecute(null) && viewModel.OpenFolderCommand.CanExecute(null),
                    "Startup must open the usable main window directly without login or an authorization Key.");
                Check(!FindVisual<Button>(window).Any(button => Equals(button.Content, "登出")),
                    "The direct-access workspace must not retain an account toolbar.");
                CheckDefaultOutputDirectory();
                await CheckUiAsync(window, viewModel, dialogs);
                viewModel.ReportMarkdownPreviewFailure(new InvalidOperationException("synthetic HTML load failure"), initializationFailure: false);
                Check(dialogs.Errors.Last().Title == "Markdown 報告載入失敗" && !dialogs.Errors.Last().Message.Contains("啟動失敗", StringComparison.Ordinal), "HTML load failure was mislabeled as WebView2 initialization failure.");
                if (Environment.GetEnvironmentVariable("PMA_WPF_CAPTURE") == "1") CaptureWindow(window);
                var oldOutput = viewModel.SelectedDocument!.AnalysisOutputPath;
                window.Close();
                var previewRoot = Path.Combine(viewModel.OutputDirectory, ".preview");
                Check(!Directory.Exists(previewRoot) || !Directory.EnumerateFiles(previewRoot, "*.html", SearchOption.AllDirectories).Any(), "Window close retained owned preview HTML files.");
                Check(viewModel.Documents.Count == 0 && viewModel.Logs.Count == 0
                    && viewModel.SelectedDocument is null && window.IsDisposed && File.Exists(oldOutput)
                    && !viewModel.OpenFileCommand.CanExecute(null),
                    "Closing the main window must release its workspace and retain generated files.");
                Console.WriteLine("WPF checks passed: direct startup, usable commands, close/disposal, retained output, opening files/folder, source and analysis bindings, Markdown preview, .NET 8/10 translation, diff, save, status and log.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                exitCode = 1;
            }
            finally { app.Shutdown(); }
        }));

        try { app.Run(); return exitCode; }
        finally { Directory.Delete(Scratch, recursive: true); }
    }

    private static void CheckDefaultOutputDirectory()
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Scratch);
            var window = new MainWindowFactory().Create();
            try
            {
                Check(((MainViewModel)window.DataContext).OutputDirectory == Path.Combine(AppContext.BaseDirectory, "output"),
                    "Default output must be beside the executable, independent of current directory or a parent solution.");
            }
            finally { window.Close(); }
        }
        finally { Directory.SetCurrentDirectory(workingDirectory); }
    }

    private static async Task CheckFourGlMarkdownRenderingAsync()
    {
        var source = new SourceDocument
        {
            FilePath = Path.Combine(Root, "synthetic.4gl"),
            FileName = "<img src=x onerror=alert(1)>#|.4gl",
            Content = "MAIN\n DEFINE message CHAR(500)\n LET message = \"<script>alert(1)</script>|'quote' ```\"\n RUN message\n SELECT '<script>alert(1)</script>| ```' FROM harmless\nEND MAIN\n# ``` <img src=x onerror=alert(1)>\n",
            Language = SourceLanguage.Informix4Gl
        };
        var parsed = await new MigrationAnalyzer().AnalyzeAsync(await new Informix4GlParser().ParseAsync(source));
        var report = new MarkdownReportGenerator().Generate(parsed, MigrationTargetFramework.Net10);
        var html = Markdig.Markdown.ToHtml(report, new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
        Check(!html.Contains("<script>", StringComparison.OrdinalIgnoreCase) && !html.Contains("<img ", StringComparison.OrdinalIgnoreCase), "4GL evidence escaped into executable Markdown HTML.");
        Check(!html.Contains("&\\#39;", StringComparison.Ordinal) && System.Net.WebUtility.HtmlDecode(html).Contains("'quote'", StringComparison.Ordinal), "Escaping broke visible apostrophes.");
        Check(html.Contains("<pre><code", StringComparison.Ordinal) && html.Contains("alert(1)", StringComparison.Ordinal), "Evidence code blocks were lost.");
        Console.WriteLine("PASS 4GL report rendering with the production Markdig extensions, HTML payload, apostrophes, pipes and backtick fences.");
    }

    private sealed class TestApp(ScriptedDialogs dialogs) : ProgramMigrationAnalyzer.App.App
    {
        public int MainCalls { get; private set; }
        protected override MainWindow CreateMainWindow()
        {
            MainCalls++;
            var viewModel = new MainViewModel(new SourceFileService(), [new CSharpSourceParser(), new Informix4GlParser()],
                new MigrationAnalyzer(), new MarkdownReportGenerator(), new MigrationSourceTranslator(),
                new OutputWriter(Path.Combine(Scratch, "output")), dialogs);
            return new MainWindow(viewModel);
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
        Check(FindButton(window, "分析勾選項目").Command is IAsyncRelayCommand, "Batch analysis button is not bound.");
        Check(FindButton(window, "設定輸出目錄").Command is IRelayCommand, "Output directory button is not bound.");
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
        await CheckLargeMarkdownPreviewAsync(tabs, viewModel, dialogs);

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
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(viewModel.CheckedFourGlCount == 5, "Folder did not check every 4GL sample.");
        var fileList = FindVisual<ListBox>(window).Single(list => ReferenceEquals(list.ItemsSource, viewModel.Documents));
        fileList.ScrollIntoView(viewModel.Documents.First());
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var checkbox = FindVisual<CheckBox>(fileList).First();
        var checkboxOwner = (SourceDocumentViewModel)checkbox.DataContext;
        checkbox.IsChecked = false;
        Check(!checkboxOwner.IsChecked && viewModel.CheckedFourGlCount == 4, "File checkbox did not update batch selection.");
        await ClickAsync(window, "全部取消");
        Check(!FindButton(window, "分析勾選項目").IsEnabled, "Empty selection did not disable batch analysis.");
        await ClickAsync(window, "全選 .4gl");
        await ClickAsync(window, "分析勾選項目");
        Check(viewModel.BatchSucceeded == 5 && viewModel.BatchFailed == 0 &&
            viewModel.Documents.All(item => File.Exists(item.AnalysisOutputPath)), "WPF batch did not write each sample report.");
        Check(viewModel.BatchProcessed == 5 && !FindButton(window, "取消批次").IsEnabled,
            "Batch progress or cancel button retained a running state.");
        var batchDocument = viewModel.Documents.Last();
        viewModel.SelectedDocument = batchDocument;
        SelectTab(tabs, "Markdown");
        await CheckMarkdownPreviewAsync(tabs, viewModel, dialogs);

        var oldRoot = viewModel.OutputDirectory;
        var oldReports = viewModel.Documents.Select(item => item.AnalysisOutputPath).ToArray();
        var customRoot = Path.Combine(Scratch, "custom-output");
        dialogs.OutputFolders.Enqueue(customRoot);
        await ClickAsync(window, "設定輸出目錄");
        Check(viewModel.OutputDirectory == customRoot && viewModel.CurrentOutputDirectory == Path.Combine(customRoot, "4gl"),
            "Output settings did not update the root and selected source-folder destination.");
        await CheckMarkdownPreviewAsync(tabs, viewModel, dialogs);
        var oldPreview = Path.Combine(oldRoot, ".preview");
        for (var attempt = 0; attempt < 100 && Directory.Exists(oldPreview) && Directory.GetFiles(oldPreview, "*.html", SearchOption.AllDirectories).Any(); attempt++)
            await Task.Delay(50);
        Check(!Directory.Exists(oldPreview) || !Directory.GetFiles(oldPreview, "*.html", SearchOption.AllDirectories).Any(),
            "Switching output roots retained the old owned preview HTML.");
        dialogs.OutputFolders.Enqueue(null);
        await ClickAsync(window, "設定輸出目錄");
        Check(viewModel.OutputDirectory == customRoot, "Canceling output selection changed the root.");
        await ClickAsync(window, "開始分析");
        await ClickAsync(window, "開始轉譯");
        Check(Path.GetDirectoryName(batchDocument.AnalysisOutputPath) == Path.Combine(customRoot, "4gl") &&
            Path.GetDirectoryName(batchDocument.TranslationOutputPath) == Path.Combine(customRoot, "4gl") && oldReports.All(File.Exists),
            "Configured root did not apply to analysis/translation or removed existing reports.");

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

    private static void CheckPreviewSessionLifetime()
    {
        var output = Path.Combine(Scratch, "preview-lifetime");
        Directory.CreateDirectory(output);
        var markdown = Path.Combine(output, "retained.md");
        File.WriteAllText(markdown, "retain report");
        var session = new MarkdownPreviewSession(output);
        var first = session.Write("<html>first</html>");
        var second = session.Write("<html>second</html>");
        var foreign = Path.Combine(Path.GetDirectoryName(second.LocalPath)!, "foreign.txt");
        File.WriteAllText(foreign, "not owned by the session");
        session.Retain(second);
        Check(!File.Exists(first.LocalPath) && File.Exists(second.LocalPath), "Old preview cleanup removed the current HTML or retained old HTML.");
        session.Dispose();
        Check(!File.Exists(second.LocalPath) && File.Exists(markdown) && File.Exists(foreign), "Preview disposal removed a deliverable/foreign file or retained its HTML.");
        Console.WriteLine("PASS preview file ownership, replacement and cleanup without deleting deliverables.");
    }

    private static async Task CheckLargeMarkdownPreviewAsync(TabControl tabs, MainViewModel viewModel, ScriptedDialogs dialogs)
    {
        var browser = FindVisual<WebView2>(tabs).FirstOrDefault();
        if (browser?.CoreWebView2 is null) { Console.WriteLine("SKIP large preview: WebView2 Runtime unavailable."); return; }
        var document = viewModel.SelectedDocument!;
        var analysis = document.Analysis!;
        var originalReport = document.Markdown;
        var originalHtml = document.RenderedMarkdownHtml;
        var outputPath = document.AnalysisOutputPath;
        var errorCount = dialogs.Errors.Count;
        var large = "<html><body>" + new string('x', 2_200_000) + "LARGE_REPORT_END</body></html>";
        document.ApplyAnalysis(analysis, originalReport, large, outputPath);
        viewModel.SelectedDocument = null;
        viewModel.SelectedDocument = document;
        var loaded = false;
        for (var i = 0; i < 100; i++)
        {
            await Task.Delay(50);
            if (dialogs.Errors.Count > errorCount) break;
            if (browser.CoreWebView2.Source.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                await browser.CoreWebView2.ExecuteScriptAsync("document.body.textContent.endsWith('LARGE_REPORT_END')") == "true") { loaded = true; break; }
        }
        Check(loaded && dialogs.Errors.Count == errorCount, "Large HTML preview failed or entered text fallback.");
        Check(File.Exists(new Uri(browser.CoreWebView2.Source).LocalPath), "Local preview document was not created.");
        document.ApplyAnalysis(analysis, originalReport, originalHtml, outputPath);
        viewModel.SelectedDocument = null;
        viewModel.SelectedDocument = document;
        await CheckMarkdownPreviewAsync(tabs, viewModel, dialogs);
        Console.WriteLine("PASS real WebView2 large HTML file navigation and document replacement.");
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
                if (body.Contains(Path.GetFileNameWithoutExtension(viewModel.SelectedDocument!.FileName), StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out var location) && location.IsFile &&
                    location.LocalPath.StartsWith(Path.Combine(viewModel.OutputDirectory, ".preview") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    if (Environment.GetEnvironmentVariable("PMA_WPF_CAPTURE") == "1")
                    {
                        var path = Path.Combine(Root, ".camp_codex", "2026-10-26_remove-authentication", "markdown.png");
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
        var command = FindButton(window, title).Command;
        if (command is null || !command.CanExecute(null))
        {
            throw new InvalidOperationException($"The {title} button is unavailable.");
        }

        if (command is IAsyncRelayCommand asynchronous) await asynchronous.ExecuteAsync(null);
        else command.Execute(null);
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
        var path = Path.Combine(Root, ".camp_codex", "2026-10-26_remove-authentication", "window.png");
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
        public Queue<string?> OutputFolders { get; } = new();
        public List<(string Title, string Message)> Errors { get; } = [];
        public string? SavePath { get; set; }

        public string? SelectSourceFile() => Files.Dequeue();
        public string? SelectSourceFolder() => Folders.Dequeue();
        public string? SelectMarkdownSavePath(string suggestedFileName) => SavePath;
        public string? SelectOutputFolder(string currentDirectory) => OutputFolders.Dequeue();
        public void ShowError(string title, string message) => Errors.Add((title, message));
    }
}

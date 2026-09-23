using System.Diagnostics;
using System.IO;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Infrastructure;
using static Fixtures;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if (!File.Exists(Path.Combine(root, "ProgramMigrationAnalyzer.sln")))
{
    throw new InvalidOperationException("Phase 2 checks must run from the ProgramMigrationAnalyzer project.");
}

var scratch = Path.Combine(root, ".codex-tmp", "2026-09-23_phase2-checks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);

try
{
    await CheckFolderScanAsync();
    await CheckOutputNamesAsync();
    await CheckViewModelFlowAsync();
    Console.WriteLine("Phase 2 checks passed: folder scan, output names, background analysis, document switch, reload, failure state, WebView2 notice.");
}
finally
{
    Directory.Delete(scratch, recursive: true);
}

async Task CheckFolderScanAsync()
{
    var folder = Path.Combine(scratch, "scan");
    foreach (var name in new[] { "bin", "obj", "output", "nested", "nested/bin" })
    {
        Directory.CreateDirectory(Path.Combine(folder, name));
    }

    File.WriteAllText(Path.Combine(folder, "main.cs"), "class A {}");
    File.WriteAllText(Path.Combine(folder, "nested", "keep.4gl"), "MAIN\nEND MAIN");
    foreach (var name in new[] { "bin", "obj", "output", "nested/bin" })
    {
        File.WriteAllText(Path.Combine(folder, name, "skip.cs"), "class Skip {}");
    }

    var documents = await new SourceFileService().LoadFolderAsync(folder);
    Check(documents.Count == 2 && documents.Any(item => item.FileName == "main.cs") &&
        documents.Any(item => item.FileName == "keep.4gl"), "Folder scan entered an excluded directory.");
}

async Task CheckOutputNamesAsync()
{
    var writer = new OutputWriter(Path.Combine(scratch, "reports"));
    var first = Document(Path.Combine(scratch, "one", "same.cs"), "first");
    var second = Document(Path.Combine(scratch, "two", "same.cs"), "second");
    var firstReport = await writer.WriteAnalysisAsync(first, "first");
    var secondReport = await writer.WriteAnalysisAsync(second, "second");
    Check(firstReport != secondReport && File.ReadAllText(firstReport) == "first" &&
        File.ReadAllText(secondReport) == "second", "Same-name reports collided.");

    var firstCode = await writer.WriteTranslationAsync(Translation(first, "first"));
    var secondCode = await writer.WriteTranslationAsync(Translation(second, "second"));
    Check(firstCode != secondCode && File.ReadAllText(firstCode) == "first" &&
        File.ReadAllText(secondCode) == "second", "Same-name translations collided.");
}

async Task CheckViewModelFlowAsync()
{
    var pathA = Path.Combine(scratch, "A.cs");
    var pathB = Path.Combine(scratch, "B.cs");
    var files = new FakeFiles();
    files.Set(Document(pathA, "old"));
    files.Set(Document(pathB, "other"));
    var dialogs = new FakeDialogs();
    var parser = new FakeParser();
    var translator = new FakeTranslator();
    var viewModel = new MainViewModel(files, [parser], new FakeAnalyzer(),
        new FakeReport(), translator, new OutputWriter(Path.Combine(scratch, "vm-output")), dialogs);

    dialogs.NextFile = pathA;
    await viewModel.OpenFileCommand.ExecuteAsync(null);
    var a = viewModel.SelectedDocument!;
    dialogs.NextFile = pathB;
    await viewModel.OpenFileCommand.ExecuteAsync(null);
    var b = viewModel.SelectedDocument!;

    viewModel.SelectedDocument = a;
    parser.Block();
    var watch = Stopwatch.StartNew();
    var analysisTask = viewModel.AnalyzeCommand.ExecuteAsync(null);
    watch.Stop();
    Check(watch.ElapsedMilliseconds < 200, "Analysis blocked the calling thread.");
    await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    viewModel.SelectedDocument = b;
    parser.Release();
    await analysisTask;
    Check(a.Analysis is not null && b.Analysis is null && a.Markdown == "report:A.cs" &&
        b.Markdown == string.Empty, "Analysis was applied to the selected document instead of its owner.");

    viewModel.SelectedDocument = a;
    translator.Block();
    var translationTask = viewModel.TranslateCommand.ExecuteAsync(null);
    await translator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    viewModel.SelectedDocument = b;
    translator.Release();
    await translationTask;
    Check(a.TranslationCode == "translated:A.cs" && b.TranslationCode == string.Empty,
        "Translation was applied to the wrong document.");

    files.Set(Document(pathA, "new"));
    dialogs.NextFile = pathA;
    await viewModel.OpenFileCommand.ExecuteAsync(null);
    var reloaded = viewModel.SelectedDocument!;
    Check(!ReferenceEquals(a, reloaded) && reloaded.SourceText == "new" &&
        reloaded.Analysis is null && reloaded.TranslationCode == string.Empty,
        "Reopening the same path displayed stale content.");

    reloaded.ApplyAnalysis(Result(reloaded.Model), "stale report", "<p>stale report</p>", "old-report.md");
    reloaded.ApplyTranslation(Translation(reloaded.Model, "stale code"), "old-code.cs");
    parser.Fail = true;
    await viewModel.AnalyzeCommand.ExecuteAsync(null);
    Check(reloaded.Status == AnalysisStatus.Failed && reloaded.Analysis is null &&
        reloaded.AnalysisView is null && reloaded.Markdown == string.Empty &&
        reloaded.RenderedMarkdownHtml == string.Empty &&
        reloaded.AnalysisOutputPath == string.Empty && reloaded.TranslationCode == string.Empty &&
        reloaded.TranslationOutputPath == string.Empty, "Failed analysis kept stale results.");

    parser.Fail = false;
    await viewModel.AnalyzeCommand.ExecuteAsync(null);
    reloaded.ApplyTranslation(Translation(reloaded.Model, "stale code"), "old-code.cs");
    translator.Fail = true;
    await viewModel.TranslateCommand.ExecuteAsync(null);
    Check(reloaded.Analysis is not null && reloaded.Markdown != string.Empty &&
        reloaded.TranslationCode == string.Empty && reloaded.TranslationOutputPath == string.Empty &&
        reloaded.Status == AnalysisStatus.Failed, "Failed translation kept stale output or removed valid analysis.");

    viewModel.ReportMarkdownPreviewFailure(new InvalidOperationException("runtime missing"));
    Check(dialogs.Errors.Any(item => item.Contains("WebView2")) &&
        viewModel.Logs.Any(item => item.Contains("WebView2")), "WebView2 failure was not shown and logged.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static class Fixtures
{
    public static SourceDocument Document(string path, string content) => new()
    {
        FilePath = path,
        FileName = Path.GetFileName(path),
        Content = content,
        Language = SourceLanguage.CSharp
    };

    public static AnalysisResult Result(SourceDocument source) => new()
    {
        Source = source,
        Language = source.Language
    };

    public static TranslationResult Translation(SourceDocument source, string code) => new()
    {
        Source = source,
        TargetFramework = MigrationTargetFramework.Net10,
        GeneratedCode = code
    };
}

sealed class FakeFiles : ISourceFileService
{
    private readonly Dictionary<string, SourceDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    public void Set(SourceDocument document) => _documents[document.FilePath] = document;
    public Task<SourceDocument> LoadFileAsync(string path, SourceLanguage languageOverride = SourceLanguage.Unknown,
        CancellationToken cancellationToken = default) => Task.FromResult(_documents[path]);
    public Task<IReadOnlyList<SourceDocument>> LoadFolderAsync(string path,
        SourceLanguage languageOverride = SourceLanguage.Unknown, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SourceDocument>>([]);
}

sealed class FakeDialogs : IFileDialogService
{
    public string? NextFile { get; set; }
    public List<string> Errors { get; } = [];
    public string? SelectSourceFile() => NextFile;
    public string? SelectSourceFolder() => null;
    public string? SelectMarkdownSavePath(string suggestedFileName) => null;
    public void ShowError(string title, string message) => Errors.Add($"{title}: {message}");
}

sealed class FakeParser : ISourceParser
{
    private ManualResetEventSlim? _gate;
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Fail { get; set; }
    public bool CanParse(SourceDocument source) => true;
    public void Block()
    {
        _gate = new ManualResetEventSlim();
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public void Release() => _gate?.Set();
    public Task<AnalysisResult> ParseAsync(SourceDocument source, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        if (_gate is not null)
        {
            if (!_gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Parser gate timed out.");
            _gate = null;
        }
        if (Fail) throw new InvalidOperationException("parser failed");
        return Task.FromResult(Result(source));
    }
}

sealed class FakeAnalyzer : ISourceAnalyzer
{
    public Task<AnalysisResult> AnalyzeAsync(AnalysisResult result, CancellationToken cancellationToken = default) =>
        Task.FromResult(result);
}

sealed class FakeReport : IMarkdownReportGenerator
{
    public string Generate(AnalysisResult result, MigrationTargetFramework targetFramework) =>
        $"report:{result.Source.FileName}";
}

sealed class FakeTranslator : ISourceTranslator
{
    private ManualResetEventSlim? _gate;
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Fail { get; set; }
    public bool CanTranslate(SourceLanguage language) => true;
    public void Block()
    {
        _gate = new ManualResetEventSlim();
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public void Release() => _gate?.Set();
    public Task<TranslationResult> TranslateAsync(AnalysisResult analysis,
        MigrationTargetFramework targetFramework, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        if (_gate is not null)
        {
            if (!_gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Translator gate timed out.");
            _gate = null;
        }
        if (Fail) throw new InvalidOperationException("translator failed");
        return Task.FromResult(Translation(analysis.Source, $"translated:{analysis.Source.FileName}"));
    }
}

using System.IO;
using CommunityToolkit.Mvvm.Input;
using ProgramMigrationAnalyzer.Analysis;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Infrastructure;
using ProgramMigrationAnalyzer.Parsers;

internal static class BatchChecks
{
    public static async Task RunAsync(string scratch)
    {
        var folder = Path.Combine(scratch, "batch-input");
        Directory.CreateDirectory(Path.Combine(folder, "a"));
        Directory.CreateDirectory(Path.Combine(folder, "b"));
        var first = Path.Combine(folder, "a", "same.4gl");
        var second = Path.Combine(folder, "b", "same.4gl");
        await File.WriteAllTextAsync(first, "MAIN\n DISPLAY \"one\"\nEND MAIN\n");
        await File.WriteAllTextAsync(second, "MAIN\n DISPLAY \"two\"\nEND MAIN\n");
        await File.WriteAllBytesAsync(Path.Combine(folder, "bad.4gl"), [0x81]);
        await File.WriteAllTextAsync(Path.Combine(folder, "skip.4gl"), "MAIN\nEND MAIN\n");
        await File.WriteAllTextAsync(Path.Combine(folder, "form.per"), "SCREEN\nEND\n");
        await File.WriteAllTextAsync(Path.Combine(folder, "other.cs"), "class Other {}");
        var dialogs = new BatchDialogs { Folder = folder };
        using var vm = Create(dialogs, Path.Combine(scratch, "batch-output"));

        var analyze = vm.AnalyzeCheckedCommand;
        await vm.OpenFolderCommand.ExecuteAsync(null);
        Check(vm.Documents.Count == 6, "Folder scan lost an unreadable source or nested files.");
        Check(vm.CheckedFourGlCount == 4, "Folder must check only .4gl, including nested and unreadable files.");
        vm.ClearBatchSelectionCommand.Execute(null);
        Check(!analyze.CanExecute(null), "Empty selection still allows batch analysis.");
        vm.SelectAllFourGlCommand.Execute(null);
        var skip = vm.Documents.Single(d => d.FileName == "skip.4gl");
        skip.IsChecked = false;
        var owner = vm.Documents.Single(d => d.FilePath == first);
        vm.SelectedDocument = owner;
        await analyze.ExecuteAsync(null);
        Check(vm.BatchSucceeded == 2 && vm.BatchFailed == 1,
            "Batch must keep successful files after a decoding failure.");
        Check(vm.Documents.Where(d => d.FileName == "same.4gl").All(d => d.Status == AnalysisStatus.Completed),
            "Nested same-name sources did not complete.");
        Check(skip.Analysis is null && string.IsNullOrEmpty(skip.AnalysisOutputPath), "Unchecked source was analyzed.");
        var reports = Directory.GetFiles(vm.OutputDirectory, "*.analysis.md", SearchOption.AllDirectories);
        Check(reports.Length == 2 && reports.Distinct().Count() == 2, "Same-name batch reports collided or non-4GL files were analyzed.");
        Check(Path.GetDirectoryName(owner.AnalysisOutputPath) == Path.Combine(vm.OutputDirectory, "batch-input", "a"),
            "Folder reports did not preserve the selected folder name and relative subfolder.");
        Check(dialogs.Errors.Count == 0, "Batch displayed a blocking dialog for a per-file failure.");
        Check(vm.BatchFailures.Count == 1,
            "Failure summary did not retain the unreadable file.");
        Check(vm.Documents.All(d => d.RenderedMarkdownHtml.Length == 0), "Batch eagerly rendered report HTML.");
        Check(vm.MarkdownHtml.Contains("<html", StringComparison.Ordinal), "Selected batch report cannot be previewed.");
        var batchReport = File.ReadAllText(owner.AnalysisOutputPath);
        await vm.AnalyzeCommand.ExecuteAsync(null);
        Check(File.ReadAllText(owner.AnalysisOutputPath) == batchReport, "Single and batch reports differ for the same source.");

        await CheckCancellationAsync(scratch);
        await CheckCanceledReadRetryAsync(scratch);
        await CheckSnapshotAndDisposalAsync(scratch);
        await CheckReadParseAndWriteFailuresAsync(scratch);
        await CheckManyFilesAsync(scratch);
        await CheckAtomicOutputAsync(scratch);
        await CheckOutputConfigurationAsync(scratch);
        Console.WriteLine("PASS 4GL batch selection, nested same-name output, unreadable source isolation, single/batch equivalence, cancellation and lazy previews.");
    }

    private static async Task CheckCancellationAsync(string scratch)
    {
        var folder = Path.Combine(scratch, "cancel-input");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 3; i++) await File.WriteAllTextAsync(Path.Combine(folder, $"{i}.4gl"), "MAIN\nEND MAIN\n");
        var dialogs = new BatchDialogs { Folder = folder };
        var parser = new BlockingParser();
        using var vm = Create(dialogs, Path.Combine(scratch, "cancel-output"), parser);
        await vm.OpenFolderCommand.ExecuteAsync(null);
        var task = vm.AnalyzeCheckedCommand.ExecuteAsync(null);
        await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(vm.BatchSucceeded == 1 && Directory.GetFiles(vm.OutputDirectory, "*.analysis.md", SearchOption.AllDirectories).Length == 1,
            "Completed report was not saved before starting the next file.");
        vm.SelectedDocument = vm.Documents.Last();
        vm.CancelBatchCommand.Execute(null);
        parser.Release.TrySetResult();
        await task;
        Check(vm.BatchSucceeded == 1 && vm.BatchFailed == 0,
            "Cancellation was counted as failure or started another source.");
        Check(vm.Documents.Last().Analysis is null && vm.Documents.All(d => d.Status != AnalysisStatus.Analyzing),
            "Cancel analyzed pending source or left a running status.");
        Check(File.Exists(vm.Documents.First().AnalysisOutputPath), "Cancel removed a completed report.");
        Check(!vm.IsBusy && vm.OpenFileCommand.CanExecute(null), "Cancel left the workspace busy.");
    }

    private static async Task CheckCanceledReadRetryAsync(string scratch)
    {
        var folder = Path.Combine(scratch, "late-read-input");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "0.4gl"), "MAIN\nEND MAIN\n");
        var second = Path.Combine(folder, "1.4gl");
        await File.WriteAllTextAsync(second, "MAIN\nEND MAIN\n");
        var files = new DelayedFiles();
        using var vm = new MainViewModel(files, [new Informix4GlParser()], new MigrationAnalyzer(), new MarkdownReportGenerator(),
            new FakeTranslator(), new OutputWriter(Path.Combine(scratch, "late-read-output")), new BatchDialogs { Folder = folder });
        await vm.OpenFolderCommand.ExecuteAsync(null);
        var task = vm.AnalyzeCheckedCommand.ExecuteAsync(null);
        await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.CancelBatchCommand.Execute(null);
        await task;
        files.LateRead.TrySetException(new IOException("Late read failure"));
        await File.WriteAllTextAsync(second, "MAIN\n DISPLAY \"repaired\"\nEND MAIN\n");
        await vm.AnalyzeCheckedCommand.ExecuteAsync(null);
        Check(vm.BatchSucceeded == 2 && vm.BatchFailed == 0 && vm.Documents.Last().SourceText.Contains("repaired", StringComparison.Ordinal),
            "Retry reused a stale faulted read after the canceled source was repaired.");
        Check(files.ReadCancellation.IsCancellationRequested, "Cancel stopped waiting but did not cancel the batch-owned source read.");
    }

    private static async Task CheckManyFilesAsync(string scratch)
    {
        var folder = Path.Combine(scratch, "many-input");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 300; i++) await File.WriteAllTextAsync(Path.Combine(folder, $"p{i:000}.4gl"), "MAIN\nEND MAIN\n");
        using var vm = Create(new BatchDialogs { Folder = folder }, Path.Combine(scratch, "many-output"));
        await vm.OpenFolderCommand.ExecuteAsync(null);
        Check(vm.Documents.Count(d => !d.Model.IsContentLoaded) >= 299,
            "Folder load eagerly read hundreds of source files.");
        await vm.AnalyzeCheckedCommand.ExecuteAsync(null);
        Check(vm.BatchSucceeded == 300 && Directory.GetFiles(vm.OutputDirectory, "*.analysis.md", SearchOption.AllDirectories).Length == 300,
            "Batch did not produce one report per selected source.");
        Check(vm.Documents.All(d => d.RenderedMarkdownHtml.Length == 0), "Hundreds of HTML previews were retained.");
    }

    private static async Task CheckSnapshotAndDisposalAsync(string scratch)
    {
        var folder = Path.Combine(scratch, "snapshot-input");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 3; i++) await File.WriteAllTextAsync(Path.Combine(folder, $"{i}.4gl"), "MAIN\nEND MAIN\n");
        var dialogs = new BatchDialogs { Folder = folder };
        var parser = new BlockingParser();
        using (var vm = new MainViewModel(new SourceFileService(), [parser], new MigrationAnalyzer(),
                   new FrameworkReport(), new FakeTranslator(), new OutputWriter(Path.Combine(scratch, "snapshot-output")), dialogs))
        {
            await vm.OpenFolderCommand.ExecuteAsync(null);
            var task = vm.AnalyzeCheckedCommand.ExecuteAsync(null);
            await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            vm.Documents.Last().IsChecked = false;
            vm.SelectedDocument = vm.Documents.Last();
            vm.SelectedLanguageOption = vm.LanguageOptions.Single(option => option.Language == SourceLanguage.CSharp);
            vm.SelectedTargetOption = vm.TargetOptions.Single(option => option.Framework == MigrationTargetFramework.Net8);
            Check(!vm.OpenFolderCommand.CanExecute(null) && !vm.SelectAllFourGlCommand.CanExecute(null) && !vm.ConfigureOutputDirectoryCommand.CanExecute(null),
                "Running batch allowed folder replacement or changing the selection.");
            parser.Release.TrySetResult();
            await task;
            Check(vm.BatchSucceeded == 3 && vm.Documents.All(document => File.ReadAllText(document.AnalysisOutputPath) == "framework:Net10"),
                "Selection or settings changed the active batch snapshot.");
            Check(vm.Documents.Last().Analysis?.Source.Language == SourceLanguage.Informix4Gl,
                "Batch used the changed language instead of the .4gl parser.");
        }

        var closingParser = new BlockingParser();
        using var closing = Create(dialogs, Path.Combine(scratch, "closing-output"), closingParser);
        await closing.OpenFolderCommand.ExecuteAsync(null);
        var references = closing.Documents.ToArray();
        var pending = closing.AnalyzeCheckedCommand.ExecuteAsync(null);
        await closingParser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        closing.Dispose();
        closingParser.Release.TrySetResult();
        await pending;
        Check(closing.Documents.Count == 0 && closing.Logs.Count == 0 && !closing.IsBatchRunning &&
            !closing.AnalyzeCheckedCommand.CanExecute(null) && references.All(document => document.SourceText.Length == 0),
            "Closing a batch retained source data, late results or enabled commands.");
        Check(Directory.GetFiles(closing.OutputDirectory, "*.analysis.md", SearchOption.AllDirectories).Length == 1,
            "Closing removed a completed report or wrote a late report.");
    }

    private static async Task CheckReadParseAndWriteFailuresAsync(string scratch)
    {
        var folder = Path.Combine(scratch, "failure-input");
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "0-write", "1-parse", "2-missing", "3-ok", "4-ok" })
            await File.WriteAllTextAsync(Path.Combine(folder, name + ".4gl"), "MAIN\nEND MAIN\n");
        var output = Path.Combine(scratch, "failure-output");
        var writer = new OutputWriter(output);
        var source = await new SourceFileService().LoadFileAsync(Path.Combine(folder, "0-write.4gl"));
        var conflict = await writer.WriteAnalysisAsync(source, "fixture", sourceRootDirectory: folder);
        File.Delete(conflict);
        Directory.CreateDirectory(conflict);
        using var vm = Create(new BatchDialogs { Folder = folder }, output, new FailingParser());
        await vm.OpenFolderCommand.ExecuteAsync(null);
        File.Delete(Path.Combine(folder, "2-missing.4gl"));
        await vm.AnalyzeCheckedCommand.ExecuteAsync(null);
        Check(vm.BatchSucceeded == 2 && vm.BatchFailed == 3 && vm.BatchProcessed == 5 && vm.BatchFailures.Count == 3,
            "Read, parse or output failure stopped later files or lost the failure summary.");
        Check(vm.Documents.Where(document => document.FileName.EndsWith("ok.4gl", StringComparison.Ordinal))
                .All(document => document.Status == AnalysisStatus.Completed && File.Exists(document.AnalysisOutputPath)),
            "Files after failures did not produce reports.");
        Check(!Directory.GetFiles(output, "*", SearchOption.AllDirectories).Any(path => path.EndsWith(".tmp", StringComparison.Ordinal)),
            "Failed report output retained a staging file.");
    }

    private static async Task CheckAtomicOutputAsync(string scratch)
    {
        var folder = Path.Combine(scratch, "atomic-output");
        var writer = new OutputWriter(folder);
        var source = new SourceDocument { FilePath = Path.Combine(scratch, "atomic.4gl"), FileName = "atomic.4gl", Content = "", Language = SourceLanguage.Informix4Gl };
        var path = await writer.WriteAnalysisAsync(source, "complete report");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await writer.WriteAnalysisAsync(source, new string('x', 100000), cancellation.Token); }
        catch (OperationCanceledException) { }
        Check(File.ReadAllText(path) == "complete report" && Directory.GetFiles(folder).Length == 1,
            "Canceled output truncated the previous report or retained a staging file.");
    }

    private static async Task CheckOutputConfigurationAsync(string scratch)
    {
        var firstRoot = Path.Combine(scratch, "來源 A");
        var secondRoot = Path.Combine(scratch, "來源 B");
        Directory.CreateDirectory(Path.Combine(firstRoot, "nested"));
        Directory.CreateDirectory(secondRoot);
        await File.WriteAllTextAsync(Path.Combine(firstRoot, "nested", "same.4gl"), "MAIN\nEND MAIN\n");
        await File.WriteAllTextAsync(Path.Combine(secondRoot, "same.4gl"), "MAIN\nEND MAIN\n");
        var dialogs = new BatchDialogs { Folder = firstRoot };
        var originalOutput = Path.Combine(scratch, "original-root");
        using var vm = Create(dialogs, originalOutput);
        await vm.OpenFolderCommand.ExecuteAsync(null);
        dialogs.Folder = secondRoot;
        await vm.OpenFolderCommand.ExecuteAsync(null);
        var first = vm.Documents.First();
        var second = vm.Documents.Last();
        var customRoot = Path.Combine(scratch, "自訂輸出 目錄");
        dialogs.OutputFolder = customRoot;
        vm.ConfigureOutputDirectoryCommand.Execute(null);
        vm.SelectedDocument = second;
        Check(vm.OutputDirectory == customRoot && vm.CurrentOutputDirectory == Path.Combine(customRoot, "來源 B"),
            "Setting output root or selecting a different source folder did not update the displayed destination.");
        await vm.AnalyzeCheckedCommand.ExecuteAsync(null);
        Check(vm.BatchSucceeded == 2 && Path.GetDirectoryName(first.AnalysisOutputPath) == Path.Combine(customRoot, "來源 A", "nested") &&
            Path.GetDirectoryName(second.AnalysisOutputPath) == Path.Combine(customRoot, "來源 B"),
            "Mixed source folders lost their selected roots after changing the output root.");
        Check(!Directory.Exists(originalOutput), "Changing output root still wrote reports to the old root.");
        dialogs.OutputFolder = null;
        vm.ConfigureOutputDirectoryCommand.Execute(null);
        Check(vm.OutputDirectory == customRoot, "Canceling output selection reset the configured root.");
        var oldPath = first.AnalysisOutputPath;
        var previous = File.ReadAllText(oldPath);
        dialogs.OutputFolder = Path.Combine(scratch, "next-root");
        vm.ConfigureOutputDirectoryCommand.Execute(null);
        vm.SelectedDocument = first;
        await vm.AnalyzeCommand.ExecuteAsync(null);
        await vm.TranslateCommand.ExecuteAsync(null);
        var expected = Path.Combine(scratch, "next-root", "來源 A", "nested");
        Check(Path.GetDirectoryName(first.AnalysisOutputPath) == expected && Path.GetDirectoryName(first.TranslationOutputPath) == expected &&
            File.ReadAllText(oldPath) == previous, "Single-file analysis/translation lost the folder group or moved an old report.");

        var outside = new SourceDocument { FilePath = Path.Combine(scratch, "unrelated.4gl"), FileName = "unrelated.4gl", Content = "" };
        var writer = new OutputWriter(Path.Combine(scratch, "boundary-output"));
        var rejected = false;
        try { await writer.WriteAnalysisAsync(outside, "must not write", sourceRootDirectory: firstRoot); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !Directory.Exists(writer.OutputDirectory), "Out-of-scope output context was accepted or wrote files.");
    }

    private static MainViewModel Create(BatchDialogs dialogs, string output, ISourceParser? parser = null) =>
        new(new SourceFileService(), [parser ?? new Informix4GlParser()], new MigrationAnalyzer(),
            new MarkdownReportGenerator(), new FakeTranslator(), new OutputWriter(output), dialogs);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class BatchDialogs : IFileDialogService
    {
        public string? Folder { get; set; }
        public string? OutputFolder { get; set; }
        public List<string> Errors { get; } = [];
        public string? SelectSourceFile() => null;
        public string? SelectSourceFolder() => Folder;
        public string? SelectMarkdownSavePath(string suggestedFileName) => null;
        public string? SelectOutputFolder(string currentDirectory) => OutputFolder;
        public void ShowError(string title, string message) => Errors.Add($"{title}: {message}");
    }

    private sealed class BlockingParser : ISourceParser
    {
        private int _calls;
        private readonly Informix4GlParser _parser = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanParse(SourceDocument source) => _parser.CanParse(source);
        public async Task<AnalysisResult> ParseAsync(SourceDocument source, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await _parser.ParseAsync(source, cancellationToken);
        }
    }

    private sealed class FrameworkReport : IMarkdownReportGenerator
    {
        public string Generate(AnalysisResult result, MigrationTargetFramework targetFramework) => $"framework:{targetFramework}";
    }

    private sealed class DelayedFiles : ISourceFileService
    {
        private readonly SourceFileService _files = new();
        private int _reads;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SourceDocument> LateRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ReadCancellation { get; private set; }
        public Task<IReadOnlyList<SourceDocument>> ScanFolderAsync(string path, SourceLanguage languageOverride = SourceLanguage.Unknown,
            CancellationToken cancellationToken = default) => _files.ScanFolderAsync(path, languageOverride, cancellationToken);
        public Task<IReadOnlyList<SourceDocument>> LoadFolderAsync(string path, SourceLanguage languageOverride = SourceLanguage.Unknown,
            CancellationToken cancellationToken = default) => _files.LoadFolderAsync(path, languageOverride, cancellationToken);
        public Task<SourceDocument> LoadFileAsync(string path, SourceLanguage languageOverride = SourceLanguage.Unknown,
            CancellationToken cancellationToken = default)
        {
            if (Path.GetFileName(path) == "1.4gl" && Interlocked.Increment(ref _reads) == 1)
            {
                ReadCancellation = cancellationToken;
                Entered.TrySetResult();
                return LateRead.Task;
            }
            return _files.LoadFileAsync(path, languageOverride, cancellationToken);
        }
    }

    private sealed class FailingParser : ISourceParser
    {
        private readonly Informix4GlParser _parser = new();
        public bool CanParse(SourceDocument source) => _parser.CanParse(source);
        public Task<AnalysisResult> ParseAsync(SourceDocument source, CancellationToken cancellationToken = default) =>
            source.FileName == "1-parse.4gl" ? throw new InvalidDataException("Synthetic parse failure") : _parser.ParseAsync(source, cancellationToken);
    }
}

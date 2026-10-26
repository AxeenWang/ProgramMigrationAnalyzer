using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Markdig;
using ProgramMigrationAnalyzer.App.Models;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISourceFileService _fileService;
    private readonly IReadOnlyList<ISourceParser> _parsers;
    private readonly ISourceAnalyzer _analyzer;
    private readonly IMarkdownReportGenerator _reportGenerator;
    private readonly ISourceTranslator _translator;
    private readonly IOutputWriter _outputWriter;
    private readonly IFileDialogService _dialogs;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private bool _disposed;
    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private readonly string _emptyMarkdownHtml;

    public MainViewModel(
        ISourceFileService fileService,
        IEnumerable<ISourceParser> parsers,
        ISourceAnalyzer analyzer,
        IMarkdownReportGenerator reportGenerator,
        ISourceTranslator translator,
        IOutputWriter outputWriter,
        IFileDialogService dialogs)
    {
        _fileService = fileService;
        _parsers = parsers.ToList();
        _analyzer = analyzer;
        _reportGenerator = reportGenerator;
        _translator = translator;
        _outputWriter = outputWriter;
        _dialogs = dialogs;
        _lifetimeToken = _lifetimeCancellation.Token;
        _emptyMarkdownHtml = BuildHtml(string.Empty);

        LanguageOptions =
        [
            new LanguageOption("Auto", SourceLanguage.Unknown),
            new LanguageOption("C#", SourceLanguage.CSharp),
            new LanguageOption("4GL", SourceLanguage.Informix4Gl)
        ];
        TargetOptions =
        [
            new TargetOption(".NET 8", MigrationTargetFramework.Net8),
            new TargetOption(".NET 10", MigrationTargetFramework.Net10)
        ];
        SelectedLanguageOption = LanguageOptions[0];
        SelectedTargetOption = TargetOptions[1];
    }

    public ObservableCollection<SourceDocumentViewModel> Documents { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }
    public IReadOnlyList<TargetOption> TargetOptions { get; }
    public string OutputDirectory => _outputWriter.OutputDirectory;
    internal bool IsWorkspaceActive => !_disposed && !_lifetimeToken.IsCancellationRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveMarkdownCommand))]
    private SourceDocumentViewModel? selectedDocument;

    [ObservableProperty]
    private LanguageOption selectedLanguageOption = null!;

    [ObservableProperty]
    private TargetOption selectedTargetOption = null!;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveMarkdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputFolderCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "準備就緒";

    public string MarkdownHtml => string.IsNullOrEmpty(SelectedDocument?.RenderedMarkdownHtml)
        ? _emptyMarkdownHtml
        : SelectedDocument.RenderedMarkdownHtml;

    partial void OnSelectedDocumentChanged(SourceDocumentViewModel? value)
    {
        OnPropertyChanged(nameof(MarkdownHtml));
    }

    private bool CanOpen() => IsWorkspaceActive && !IsBusy;
    private bool CanAnalyze() => CanOpen() && SelectedDocument is not null;
    private bool CanTranslate() => CanOpen() && SelectedDocument?.Analysis is not null;
    private bool CanSaveMarkdown() => CanOpen() && !string.IsNullOrWhiteSpace(SelectedDocument?.Markdown);

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenFileAsync()
    {
        if (!CanOpen()) return;
        await RunGuardedAsync("載入檔案", async token =>
        {
            var path = _dialogs.SelectSourceFile();
            EnsureActive();
            if (path is null) return;
            AddLog("Loading source...");
            var document = await _fileService.LoadFileAsync(path, SelectedLanguageOption.Language, token);
            EnsureActive();
            AddOrSelect(document);
            AddLog($"Loaded {document.FileName} ({FormatLanguage(document.Language)}).");
        });
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenFolderAsync()
    {
        if (!CanOpen()) return;
        await RunGuardedAsync("載入資料夾", async token =>
        {
            var path = _dialogs.SelectSourceFolder();
            EnsureActive();
            if (path is null) return;
            AddLog("Scanning source folder...");
            var documents = await _fileService.LoadFolderAsync(path, SelectedLanguageOption.Language, token);
            EnsureActive();
            foreach (var document in documents) AddOrSelect(document, select: false);
            SelectedDocument ??= Documents.FirstOrDefault();
            AddLog($"Loaded {documents.Count} supported source file(s).");
            if (documents.Count == 0) StatusMessage = "資料夾內沒有支援的原始碼";
        });
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (!CanAnalyze()) return;
        var viewModel = SelectedDocument!;
        await RunGuardedAsync("分析", async token =>
        {
            viewModel.BeginAnalysis();
            RefreshDocumentCommands(viewModel);
            AddLog("Detecting language...");
            var source = ApplyLanguageOverride(viewModel.Model);
            var target = SelectedTargetOption.Framework;
            var parser = _parsers.FirstOrDefault(candidate => candidate.CanParse(source))
                ?? throw new NotSupportedException($"Unsupported source language for {source.FileName}.");
            EnsureActive();
            AddLog("Parsing...");
            var result = await Task.Run(() => parser.ParseAsync(source, token), token);
            EnsureActive();
            AddLog("Analyzing SQL...");
            AddLog("Checking legacy APIs...");
            result = await Task.Run(() => _analyzer.AnalyzeAsync(result, token), token);
            EnsureActive();
            AddLog("Generating Markdown...");
            var (markdown, html) = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var report = _reportGenerator.Generate(result, target);
                token.ThrowIfCancellationRequested();
                return (report, BuildHtml(report));
            }, token);
            EnsureActive();
            var outputPath = await _outputWriter.WriteAnalysisAsync(source, markdown, token);
            EnsureActive();
            viewModel.ApplyAnalysis(result, markdown, html, outputPath);
            RefreshDocumentCommands(viewModel);
            AddLog($"Completed. Report: {outputPath}");
        }, () => { viewModel.FailAnalysis(); RefreshDocumentCommands(viewModel); });
    }

    [RelayCommand(CanExecute = nameof(CanTranslate))]
    private async Task TranslateAsync()
    {
        if (!CanTranslate()) return;
        var viewModel = SelectedDocument!;
        var analysis = viewModel.Analysis!;
        await RunGuardedAsync("轉譯", async token =>
        {
            viewModel.BeginTranslation();
            var target = SelectedTargetOption;
            AddLog($"Translating to {target.Name}...");
            var result = await Task.Run(() => _translator.TranslateAsync(analysis, target.Framework, token), token);
            EnsureActive();
            var outputPath = await _outputWriter.WriteTranslationAsync(result, token);
            EnsureActive();
            viewModel.ApplyTranslation(result, outputPath);
            AddLog($"Translation completed. Output: {outputPath}");
        }, viewModel.MarkFailed);
    }

    [RelayCommand(CanExecute = nameof(CanSaveMarkdown))]
    private async Task SaveMarkdownAsync()
    {
        if (!CanSaveMarkdown()) return;
        var document = SelectedDocument!;
        var markdown = document.Markdown;
        await RunGuardedAsync("儲存報告", async token =>
        {
            var path = _dialogs.SelectMarkdownSavePath($"{document.FileName}.analysis.md");
            EnsureActive();
            if (path is null) return;
            await File.WriteAllTextAsync(path, markdown, new UTF8Encoding(false), token);
            EnsureActive();
            AddLog($"Markdown saved: {path}");
        });
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void OpenOutputFolder()
    {
        if (!CanOpen()) return;
        IsBusy = true;
        try
        {
            EnsureActive();
            Directory.CreateDirectory(OutputDirectory);
            EnsureActive();
            Process.Start(new ProcessStartInfo(OutputDirectory) { UseShellExecute = true });
        }
        catch (OperationCanceledException) when (!IsWorkspaceActive) { }
        catch (Exception exception)
        {
            if (!IsWorkspaceActive) return;
            AddLog($"ERROR: {exception.Message}");
            _dialogs.ShowError("無法開啟 Output", exception.Message);
        }
        finally { if (IsWorkspaceActive) IsBusy = false; }
    }

    private void EnsureActive()
    {
        if (!IsWorkspaceActive) throw new OperationCanceledException(_lifetimeToken);
    }

    private async Task RunGuardedAsync(string operation, Func<CancellationToken, Task> action, Action? onFailure = null)
    {
        if (!CanOpen() || !IsWorkspaceActive) return;
        IsBusy = true;
        StatusMessage = $"{operation}中...";
        try
        {
            EnsureActive();
            await action(_lifetimeToken);
            EnsureActive();
            StatusMessage = $"{operation}完成";
        }
        catch (OperationCanceledException) when (!IsWorkspaceActive || _lifetimeToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsWorkspaceActive) return;
            onFailure?.Invoke();
            StatusMessage = $"{operation}失敗";
            AddLog($"ERROR: {exception.Message}");
            _dialogs.ShowError($"{operation}失敗", exception.Message);
        }
        finally { if (IsWorkspaceActive) IsBusy = false; }
    }

    private void NotifyCommands()
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        AnalyzeCommand.NotifyCanExecuteChanged();
        TranslateCommand.NotifyCanExecuteChanged();
        SaveMarkdownCommand.NotifyCanExecuteChanged();
        OpenOutputFolderCommand.NotifyCanExecuteChanged();
    }

    private void ClearWorkspace()
    {
        foreach (var document in Documents.ToArray()) document.ClearWorkspace();
        SelectedDocument = null;
        Documents.Clear();
        Logs.Clear();
        IsBusy = false;
        StatusMessage = "工作區已關閉";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        ClearWorkspace();
        NotifyCommands();
        _lifetimeCancellation.Dispose();
    }

    private void AddOrSelect(SourceDocument document, bool select = true)
    {
        var existing = Documents.FirstOrDefault(item =>
            item.FilePath.Equals(document.FilePath, StringComparison.OrdinalIgnoreCase));
        var replacement = new SourceDocumentViewModel(document);
        if (existing is null)
        {
            Documents.Add(replacement);
        }
        else
        {
            var wasSelected = ReferenceEquals(SelectedDocument, existing);
            Documents[Documents.IndexOf(existing)] = replacement;
            select |= wasSelected;
        }

        if (select)
        {
            SelectedDocument = replacement;
        }
    }

    private void RefreshDocumentCommands(SourceDocumentViewModel document)
    {
        if (ReferenceEquals(SelectedDocument, document))
        {
            OnPropertyChanged(nameof(MarkdownHtml));
            TranslateCommand.NotifyCanExecuteChanged();
            SaveMarkdownCommand.NotifyCanExecuteChanged();
        }
    }

    public void ReportMarkdownPreviewFailure(Exception exception)
    {
        if (!IsWorkspaceActive) return;
        AddLog($"ERROR: WebView2 Markdown preview unavailable: {exception.Message}");
        _dialogs.ShowError("Markdown 預覽無法啟動", $"WebView2 啟動失敗，已改用純文字預覽。\n{exception.Message}");
    }

    private SourceDocument ApplyLanguageOverride(SourceDocument source)
    {
        if (SelectedLanguageOption.Language == SourceLanguage.Unknown ||
            SelectedLanguageOption.Language == source.Language)
        {
            return source;
        }

        return new SourceDocument
        {
            FilePath = source.FilePath,
            FileName = source.FileName,
            Content = source.Content,
            Language = SelectedLanguageOption.Language
        };
    }

    private void AddLog(string message)
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Logs.Count > 300)
        {
            Logs.RemoveAt(0);
        }
    }

    private string BuildHtml(string markdown)
    {
        var body = string.IsNullOrWhiteSpace(markdown)
            ? "<p class=\"empty\">完成分析後會在此顯示 Markdown 報告。</p>"
            : Markdig.Markdown.ToHtml(markdown, _markdownPipeline);
        return $$"""
                 <!doctype html>
                 <html lang="zh-Hant">
                 <head>
                   <meta charset="utf-8">
                   <style>
                     :root { color-scheme: light; background: #ffffff; }
                     body { font-family: "Segoe UI", "Microsoft JhengHei", sans-serif; background: #ffffff; color: #1f2937; margin: 24px 32px; line-height: 1.65; }
                     h1, h2, h3 { color: #17365d; }
                     h1 { border-bottom: 2px solid #dbeafe; padding-bottom: 10px; }
                     h2 { margin-top: 28px; border-bottom: 1px solid #e5e7eb; padding-bottom: 6px; }
                     code { background: #f3f4f6; padding: 2px 5px; border-radius: 4px; }
                     table { border-collapse: collapse; width: 100%; }
                     th, td { border: 1px solid #d1d5db; padding: 7px 10px; text-align: left; }
                     th { background: #eff6ff; }
                     pre { background: #f3f4f6; color: #1f2937; padding: 12px; overflow-x: auto; }
                     a { color: #1d4ed8; }
                     blockquote { border-left: 4px solid #f59e0b; margin-left: 0; padding: 8px 14px; background: #fffbeb; }
                     .empty { color: #6b7280; }
                   </style>
                 </head>
                 <body>{{body}}</body>
                 </html>
                 """;
    }

    private static string FormatLanguage(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp => "C#",
        SourceLanguage.Informix4Gl => "4GL",
        _ => "Unknown"
    };
}

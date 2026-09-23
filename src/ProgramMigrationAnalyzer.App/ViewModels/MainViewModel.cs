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

public partial class MainViewModel : ObservableObject
{
    private readonly ISourceFileService _fileService;
    private readonly IReadOnlyList<ISourceParser> _parsers;
    private readonly ISourceAnalyzer _analyzer;
    private readonly IMarkdownReportGenerator _reportGenerator;
    private readonly ISourceTranslator _translator;
    private readonly IOutputWriter _outputWriter;
    private readonly IFileDialogService _dialogs;
    private readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

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
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "準備就緒";

    public string MarkdownHtml => BuildHtml(SelectedDocument?.Markdown ?? string.Empty);

    partial void OnSelectedDocumentChanged(SourceDocumentViewModel? value)
    {
        OnPropertyChanged(nameof(MarkdownHtml));
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var path = _dialogs.SelectSourceFile();
        if (path is null)
        {
            return;
        }

        await RunGuardedAsync("載入檔案", async () =>
        {
            AddLog("Loading source...");
            var document = await _fileService.LoadFileAsync(path, SelectedLanguageOption.Language);
            AddOrSelect(document);
            AddLog($"Loaded {document.FileName} ({FormatLanguage(document.Language)}).");
        });
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var path = _dialogs.SelectSourceFolder();
        if (path is null)
        {
            return;
        }

        await RunGuardedAsync("載入資料夾", async () =>
        {
            AddLog("Scanning source folder...");
            var documents = await _fileService.LoadFolderAsync(path, SelectedLanguageOption.Language);
            foreach (var document in documents)
            {
                AddOrSelect(document, select: false);
            }

            SelectedDocument ??= Documents.FirstOrDefault();
            AddLog($"Loaded {documents.Count} supported source file(s).");
            if (documents.Count == 0)
            {
                StatusMessage = "資料夾內沒有支援的原始碼";
            }
        });
    }

    private bool CanAnalyze() => SelectedDocument is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (SelectedDocument is null)
        {
            return;
        }

        await RunGuardedAsync("分析", async () =>
        {
            var viewModel = SelectedDocument;
            viewModel.Status = AnalysisStatus.Analyzing;
            AddLog("Detecting language...");
            var source = ApplyLanguageOverride(viewModel.Model);
            var parser = _parsers.FirstOrDefault(candidate => candidate.CanParse(source));
            if (parser is null)
            {
                throw new NotSupportedException($"Unsupported source language for {source.FileName}.");
            }

            AddLog("Parsing...");
            var result = await parser.ParseAsync(source);
            AddLog("Analyzing SQL...");
            AddLog("Checking legacy APIs...");
            result = await _analyzer.AnalyzeAsync(result);
            AddLog("Generating Markdown...");
            var markdown = _reportGenerator.Generate(result, SelectedTargetOption.Framework);
            var outputPath = await _outputWriter.WriteAnalysisAsync(source, markdown);
            viewModel.ApplyAnalysis(result, markdown, outputPath);
            OnPropertyChanged(nameof(MarkdownHtml));
            TranslateCommand.NotifyCanExecuteChanged();
            SaveMarkdownCommand.NotifyCanExecuteChanged();
            AddLog($"Completed. Report: {outputPath}");
        });
    }

    private bool CanTranslate() => SelectedDocument?.Analysis is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanTranslate))]
    private async Task TranslateAsync()
    {
        if (SelectedDocument?.Analysis is null)
        {
            return;
        }

        await RunGuardedAsync("轉譯", async () =>
        {
            AddLog($"Translating to {SelectedTargetOption.Name}...");
            var result = await _translator.TranslateAsync(SelectedDocument.Analysis, SelectedTargetOption.Framework);
            var outputPath = await _outputWriter.WriteTranslationAsync(result);
            SelectedDocument.ApplyTranslation(result, outputPath);
            AddLog($"Translation completed. Output: {outputPath}");
        });
    }

    private bool CanSaveMarkdown() => !IsBusy && !string.IsNullOrWhiteSpace(SelectedDocument?.Markdown);

    [RelayCommand(CanExecute = nameof(CanSaveMarkdown))]
    private async Task SaveMarkdownAsync()
    {
        if (SelectedDocument is null)
        {
            return;
        }

        var path = _dialogs.SelectMarkdownSavePath($"{SelectedDocument.FileName}.analysis.md");
        if (path is null)
        {
            return;
        }

        await RunGuardedAsync("儲存報告", async () =>
        {
            await File.WriteAllTextAsync(path, SelectedDocument.Markdown, new UTF8Encoding(false));
            AddLog($"Markdown saved: {path}");
        });
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            Process.Start(new ProcessStartInfo(OutputDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AddLog($"ERROR: {exception.Message}");
            _dialogs.ShowError("無法開啟 Output", exception.Message);
        }
    }

    private async Task RunGuardedAsync(string operation, Func<Task> action)
    {
        IsBusy = true;
        StatusMessage = $"{operation}中...";
        try
        {
            await action();
            StatusMessage = $"{operation}完成";
        }
        catch (Exception exception)
        {
            if (SelectedDocument is not null)
            {
                SelectedDocument.Status = AnalysisStatus.Failed;
            }

            StatusMessage = $"{operation}失敗";
            AddLog($"ERROR: {exception.Message}");
            _dialogs.ShowError($"{operation}失敗", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddOrSelect(SourceDocument document, bool select = true)
    {
        var existing = Documents.FirstOrDefault(item =>
            item.FilePath.Equals(document.FilePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new SourceDocumentViewModel(document);
            Documents.Add(existing);
        }

        if (select)
        {
            SelectedDocument = existing;
        }
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
                     body { font-family: "Segoe UI", "Microsoft JhengHei", sans-serif; color: #1f2937; margin: 24px 32px; line-height: 1.65; }
                     h1, h2, h3 { color: #17365d; }
                     h1 { border-bottom: 2px solid #dbeafe; padding-bottom: 10px; }
                     h2 { margin-top: 28px; border-bottom: 1px solid #e5e7eb; padding-bottom: 6px; }
                     code { background: #f3f4f6; padding: 2px 5px; border-radius: 4px; }
                     table { border-collapse: collapse; width: 100%; }
                     th, td { border: 1px solid #d1d5db; padding: 7px 10px; text-align: left; }
                     th { background: #eff6ff; }
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

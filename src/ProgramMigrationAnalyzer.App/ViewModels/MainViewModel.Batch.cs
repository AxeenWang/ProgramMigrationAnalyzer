using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.App.ViewModels;

public sealed record BatchFailure(string FilePath, string Message);

public partial class MainViewModel
{
    private readonly ConcurrentDictionary<SourceDocumentViewModel, Task<SourceDocument>> _sourceLoads = new();
    private CancellationTokenSource? _batchCancellation;

    public ObservableCollection<BatchFailure> BatchFailures { get; } = [];
    public int FourGlCount => Documents.Count(document => document.IsBatchEligible);
    public int CheckedFourGlCount => Documents.Count(document => document.IsBatchEligible && document.IsChecked);
    public string BatchSelectionText => $"已勾選 {CheckedFourGlCount} / {FourGlCount} 支 .4gl";
    public bool CanChangeBatchSelection => CanOpen();

    [ObservableProperty]
    private bool isBatchRunning;
    [ObservableProperty]
    private int batchTotal;
    [ObservableProperty]
    private int batchProcessed;
    [ObservableProperty]
    private int batchSucceeded;
    [ObservableProperty]
    private int batchFailed;
    [ObservableProperty]
    private string batchSummary = string.Empty;

    private bool CanAnalyzeChecked() => CanOpen() && CheckedFourGlCount > 0;
    private bool CanCancelBatch() => IsWorkspaceActive && IsBatchRunning && _batchCancellation?.IsCancellationRequested == false;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void SelectAllFourGl()
    {
        foreach (var document in Documents.Where(document => document.IsBatchEligible)) document.IsChecked = true;
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void ClearBatchSelection()
    {
        foreach (var document in Documents) document.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(CanCancelBatch))]
    private void CancelBatch()
    {
        _batchCancellation?.Cancel();
        CancelBatchCommand.NotifyCanExecuteChanged();
        StatusMessage = "正在取消批次分析…";
    }

    [RelayCommand(CanExecute = nameof(CanAnalyzeChecked))]
    private async Task AnalyzeCheckedAsync()
    {
        if (!CanAnalyzeChecked()) return;
        var queue = Documents.Where(document => document.IsBatchEligible && document.IsChecked).ToArray();
        var target = SelectedTargetOption.Framework;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        _batchCancellation = cancellation;
        BatchFailures.Clear();
        BatchTotal = queue.Length;
        BatchProcessed = BatchSucceeded = BatchFailed = 0;
        BatchSummary = string.Empty;
        IsBatchRunning = true;
        IsBusy = true;
        NotifyBatchCommands();
        try
        {
            foreach (var document in queue)
            {
                if (cancellation.IsCancellationRequested) break;
                StatusMessage = $"分析 {BatchProcessed + 1}/{BatchTotal}：{document.FileName}";
                try
                {
                    await AnalyzeDocumentAsync(document, SourceLanguage.Informix4Gl, target, cancellation.Token, renderHtml: false);
                    BatchSucceeded++;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    if (IsWorkspaceActive) { document.CancelAnalysis(); RefreshDocumentCommands(document); }
                    break;
                }
                catch (Exception exception)
                {
                    if (!IsWorkspaceActive) break;
                    document.FailAnalysis(exception.Message);
                    RefreshDocumentCommands(document);
                    BatchFailed++;
                    BatchFailures.Add(new BatchFailure(document.FilePath, exception.Message));
                    AddLog($"ERROR: {document.FilePath}: {exception.Message}");
                }
                if (!IsWorkspaceActive) break;
                BatchProcessed = BatchSucceeded + BatchFailed;
            }
            if (IsWorkspaceActive)
            {
                var state = cancellation.IsCancellationRequested ? "已取消" : "完成";
                BatchSummary = $"批次{state}：成功 {BatchSucceeded}，失敗 {BatchFailed}，未處理 {BatchTotal - BatchProcessed}";
                StatusMessage = BatchSummary;
                AddLog(BatchSummary);
            }
        }
        finally
        {
            _batchCancellation = null;
            if (IsWorkspaceActive)
            {
                IsBatchRunning = false;
                IsBusy = false;
                NotifyBatchCommands();
            }
        }
    }

    private async Task AnalyzeDocumentAsync(SourceDocumentViewModel document, SourceLanguage language,
        MigrationTargetFramework target, CancellationToken token, bool renderHtml)
    {
        document.BeginAnalysis();
        RefreshDocumentCommands(document);
        await EnsureSourceLoadedAsync(document, token);
        token.ThrowIfCancellationRequested();
        var source = ApplyLanguageOverride(document.Model, language);
        var parser = _parsers.FirstOrDefault(candidate => candidate.CanParse(source))
            ?? throw new NotSupportedException($"Unsupported source language for {source.FileName}.");
        AddLog($"Parsing: {source.FileName}");
        var result = await Task.Run(() => parser.ParseAsync(source, token), token);
        EnsureActive();
        token.ThrowIfCancellationRequested();
        result = await Task.Run(() => _analyzer.AnalyzeAsync(result, token), token);
        EnsureActive();
        var (markdown, html) = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var report = _reportGenerator.Generate(result, target);
            token.ThrowIfCancellationRequested();
            return (report, renderHtml ? BuildHtml(report) : string.Empty);
        }, token);
        EnsureActive();
        var outputPath = await _outputWriter.WriteAnalysisAsync(source, markdown, token, document.SourceRootDirectory);
        // The atomic write is the commit point: a completed report counts even if cancel arrives immediately afterward.
        EnsureActive();
        document.ApplyAnalysis(result, markdown, html, outputPath);
        RefreshDocumentCommands(document);
        AddLog($"Completed. Report: {outputPath}");
    }

    private async Task EnsureSourceLoadedAsync(SourceDocumentViewModel document, CancellationToken token)
    {
        if (document.Model.IsContentLoaded) return;
        var load = _sourceLoads.GetOrAdd(document, item => _fileService.LoadFileAsync(item.FilePath, item.Model.Language, token));
        _ = load.ContinueWith(completed =>
        {
            // Observe late failures even when all waiters canceled, and never remove a newer retry.
            _ = completed.Exception;
            _sourceLoads.TryRemove(new KeyValuePair<SourceDocumentViewModel, Task<SourceDocument>>(document, completed));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var source = await load.WaitAsync(token);
        EnsureActive();
        token.ThrowIfCancellationRequested();
        if (Documents.Contains(document)) document.ApplySource(source);
    }

    private async Task LoadPreviewSourceAsync(SourceDocumentViewModel document)
    {
        if (document.Model.IsContentLoaded) return;
        try { await EnsureSourceLoadedAsync(document, _lifetimeToken); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!IsWorkspaceActive || !Documents.Contains(document)) return;
            // A selection load may overlap a batch retry. Never clear a completed result.
            if (document.Status is AnalysisStatus.Loaded or AnalysisStatus.Failed)
            {
                document.FailAnalysis(exception.Message);
                AddLog($"ERROR: {document.FilePath}: {exception.Message}");
            }
        }
    }

    private void OnDocumentSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SourceDocumentViewModel.IsChecked)) RefreshBatchSelection();
    }

    private void RefreshBatchSelection()
    {
        OnPropertyChanged(nameof(FourGlCount));
        OnPropertyChanged(nameof(CheckedFourGlCount));
        OnPropertyChanged(nameof(BatchSelectionText));
        AnalyzeCheckedCommand.NotifyCanExecuteChanged();
    }

    private void NotifyBatchCommands()
    {
        AnalyzeCheckedCommand.NotifyCanExecuteChanged();
        CancelBatchCommand.NotifyCanExecuteChanged();
        SelectAllFourGlCommand.NotifyCanExecuteChanged();
        ClearBatchSelectionCommand.NotifyCanExecuteChanged();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.App.ViewModels;

public partial class SourceDocumentViewModel(SourceDocument model) : ObservableObject
{
    public SourceDocument Model { get; } = model;
    public string FileName => Model.FileName;
    public string FilePath => Model.FilePath;
    public string SourceText => Model.Content;
    public string LanguageName => Model.Language switch
    {
        SourceLanguage.CSharp => "C#",
        SourceLanguage.Informix4Gl => "4GL",
        _ => "Unknown"
    };

    [ObservableProperty]
    private AnalysisStatus status = AnalysisStatus.Loaded;

    [ObservableProperty]
    private AnalysisResult? analysis;

    [ObservableProperty]
    private AnalysisViewModel? analysisView;

    [ObservableProperty]
    private string markdown = string.Empty;

    public string RenderedMarkdownHtml { get; private set; } = string.Empty;

    [ObservableProperty]
    private string translationCode = string.Empty;

    [ObservableProperty]
    private string analysisOutputPath = string.Empty;

    [ObservableProperty]
    private string translationOutputPath = string.Empty;

    public string StatusText => Status switch
    {
        AnalysisStatus.Loaded => "已載入",
        AnalysisStatus.Analyzing => "分析中",
        AnalysisStatus.Completed => "已完成",
        AnalysisStatus.Failed => "失敗",
        _ => Status.ToString()
    };

    partial void OnStatusChanged(AnalysisStatus value) => OnPropertyChanged(nameof(StatusText));

    public void BeginAnalysis()
    {
        ClearAnalysis();
        Status = AnalysisStatus.Analyzing;
    }

    public void FailAnalysis()
    {
        ClearAnalysis();
        Status = AnalysisStatus.Failed;
    }

    private void ClearAnalysis()
    {
        Analysis = null;
        AnalysisView = null;
        Markdown = string.Empty;
        RenderedMarkdownHtml = string.Empty;
        AnalysisOutputPath = string.Empty;
        ClearTranslation();
    }

    public void BeginTranslation() => ClearTranslation();

    public void MarkFailed() => Status = AnalysisStatus.Failed;

    private void ClearTranslation()
    {
        TranslationCode = string.Empty;
        TranslationOutputPath = string.Empty;
    }

    public void ApplyAnalysis(AnalysisResult result, string report, string renderedHtml, string outputPath)
    {
        Analysis = result;
        AnalysisView = new AnalysisViewModel(result);
        Markdown = report;
        RenderedMarkdownHtml = renderedHtml;
        AnalysisOutputPath = outputPath;
        Status = AnalysisStatus.Completed;
    }

    public void ApplyTranslation(TranslationResult result, string outputPath)
    {
        TranslationCode = result.GeneratedCode;
        TranslationOutputPath = outputPath;
        Status = AnalysisStatus.Completed;
    }
}

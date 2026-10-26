namespace ProgramMigrationAnalyzer.Core;

public interface ISourceParser
{
    bool CanParse(SourceDocument source);

    Task<AnalysisResult> ParseAsync(
        SourceDocument source,
        CancellationToken cancellationToken = default);
}

public interface ISourceAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(
        AnalysisResult result,
        CancellationToken cancellationToken = default);
}

public interface IMarkdownReportGenerator
{
    string Generate(AnalysisResult result, MigrationTargetFramework targetFramework);
}

public interface ISourceTranslator
{
    bool CanTranslate(SourceLanguage language);

    Task<TranslationResult> TranslateAsync(
        AnalysisResult analysis,
        MigrationTargetFramework targetFramework,
        CancellationToken cancellationToken = default);
}

public interface ISourceFileService
{
    async Task<IReadOnlyList<SourceDocument>> ScanFolderAsync(
        string path,
        SourceLanguage languageOverride = SourceLanguage.Unknown,
        CancellationToken cancellationToken = default) =>
        await LoadFolderAsync(path, languageOverride, cancellationToken);

    Task<SourceDocument> LoadFileAsync(
        string path,
        SourceLanguage languageOverride = SourceLanguage.Unknown,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceDocument>> LoadFolderAsync(
        string path,
        SourceLanguage languageOverride = SourceLanguage.Unknown,
        CancellationToken cancellationToken = default);
}

public interface IOutputWriter
{
    string OutputDirectory { get; }

    void SetOutputDirectory(string path);

    string GetDocumentOutputDirectory(SourceDocument source, string? sourceRootDirectory = null);

    Task<string> WriteAnalysisAsync(
        SourceDocument source,
        string markdown,
        CancellationToken cancellationToken = default,
        string? sourceRootDirectory = null);

    Task<string> WriteTranslationAsync(
        TranslationResult result,
        CancellationToken cancellationToken = default,
        string? sourceRootDirectory = null);
}

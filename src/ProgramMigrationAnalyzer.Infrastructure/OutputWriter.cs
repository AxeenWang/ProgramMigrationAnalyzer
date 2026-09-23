using System.Text;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Infrastructure;

public sealed class OutputWriter(string outputDirectory) : IOutputWriter
{
    public string OutputDirectory { get; } = Path.GetFullPath(outputDirectory);

    public async Task<string> WriteAnalysisAsync(
        SourceDocument source,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        var path = Path.Combine(OutputDirectory, $"{Sanitize(source.FileName)}.analysis.md");
        await File.WriteAllTextAsync(path, markdown, new UTF8Encoding(false), cancellationToken);
        return path;
    }

    public async Task<string> WriteTranslationAsync(
        TranslationResult result,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        var suffix = result.TargetFramework == MigrationTargetFramework.Net8 ? "net8" : "net10";
        var path = Path.Combine(OutputDirectory, $"{Sanitize(result.Source.FileName)}.{suffix}.cs");
        await File.WriteAllTextAsync(path, result.GeneratedCode, new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private void EnsureDirectory() => Directory.CreateDirectory(OutputDirectory);

    private static string Sanitize(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}

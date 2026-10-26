using System.Security.Cryptography;
using System.Text;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Infrastructure;

public sealed class OutputWriter : IOutputWriter
{
    public OutputWriter(string? outputDirectory = null) =>
        SetOutputDirectory(outputDirectory ?? Path.Combine(AppContext.BaseDirectory, "output"));

    public string OutputDirectory { get; private set; } = null!;

    public void SetOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("輸出根目錄不可為空白。", nameof(path));
        OutputDirectory = Path.GetFullPath(path);
    }

    public string GetDocumentOutputDirectory(SourceDocument source, string? sourceRootDirectory = null)
    {
        if (sourceRootDirectory is null) return OutputDirectory;
        var root = Path.GetFullPath(sourceRootDirectory);
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(source.FilePath))!;
        var relative = Path.GetRelativePath(root, sourceDirectory);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("來源檔案不在選取的資料夾範圍內。");
        var name = Sanitize(new DirectoryInfo(root).Name);
        if (string.IsNullOrEmpty(name)) name = "root";
        return relative == "." ? Path.Combine(OutputDirectory, name) : Path.Combine(OutputDirectory, name, relative);
    }

    public async Task<string> WriteAnalysisAsync(
        SourceDocument source,
        string markdown,
        CancellationToken cancellationToken = default,
        string? sourceRootDirectory = null)
    {
        var directory = GetDocumentOutputDirectory(source, sourceRootDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{GetOutputStem(source)}.analysis.md");
        var staging = Path.Combine(directory, $".{Guid.NewGuid():N}.analysis.tmp");
        try
        {
            await File.WriteAllTextAsync(staging, markdown, new UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
        return path;
    }

    public async Task<string> WriteTranslationAsync(
        TranslationResult result,
        CancellationToken cancellationToken = default,
        string? sourceRootDirectory = null)
    {
        var directory = GetDocumentOutputDirectory(result.Source, sourceRootDirectory);
        Directory.CreateDirectory(directory);
        var suffix = result.TargetFramework == MigrationTargetFramework.Net8 ? "net8" : "net10";
        var path = Path.Combine(directory, $"{GetOutputStem(result.Source)}.{suffix}.cs");
        await File.WriteAllTextAsync(path, result.GeneratedCode, new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string GetOutputStem(SourceDocument source)
    {
        var fullPath = Path.GetFullPath(source.FilePath);
        var identity = OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"{Sanitize(source.FileName)}.{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    private static string Sanitize(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}

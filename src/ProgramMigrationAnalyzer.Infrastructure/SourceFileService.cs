using System.Text;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Infrastructure;

public sealed class SourceFileService : ISourceFileService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cs", ".4gl", ".per" };

    private static readonly HashSet<string> IgnoredDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".vs", "bin", "obj", "output", ".codex-tmp", ".claude-tmp" };

    static SourceFileService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<SourceDocument> LoadFileAsync(
        string path,
        SourceLanguage languageOverride = SourceLanguage.Unknown,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A source-file path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The selected source file does not exist.", path);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var content = Decode(bytes);
        return new SourceDocument
        {
            FilePath = Path.GetFullPath(path),
            FileName = Path.GetFileName(path),
            Content = content,
            Language = languageOverride == SourceLanguage.Unknown
                ? DetectLanguage(path)
                : languageOverride
        };
    }

    public async Task<IReadOnlyList<SourceDocument>> LoadFolderAsync(
        string path,
        SourceLanguage languageOverride = SourceLanguage.Unknown,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("The selected source folder does not exist.");
        }

        var files = await Task.Run(() => EnumerateSourceFiles(path, cancellationToken), cancellationToken);

        var documents = new List<SourceDocument>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.Add(await LoadFileAsync(file, languageOverride, cancellationToken));
        }

        return documents;
    }

    public static SourceLanguage DetectLanguage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => SourceLanguage.CSharp,
            ".4gl" or ".per" => SourceLanguage.Informix4Gl,
            _ => SourceLanguage.Unknown
        };

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var value = new UTF8Encoding(false, true).GetString(bytes);
            return value.TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return Encoding.GetEncoding(950).GetString(bytes);
            }
            catch (ArgumentException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }
    }

    private static List<string> EnumerateSourceFiles(string root, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (IgnoredDirectoryNames.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SupportedExtensions.Contains(Path.GetExtension(file)))
                {
                    files.Add(file);
                }
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IgnoredDirectoryNames.Contains(Path.GetFileName(child)) &&
                    !File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(child);
                }
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }
}

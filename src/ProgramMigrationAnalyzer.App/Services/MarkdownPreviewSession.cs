using System.IO;
using System.Text;

namespace ProgramMigrationAnalyzer.App.Services;

/// <summary>Owns only this window's generated HTML. Markdown deliverables are never removed.</summary>
internal sealed class MarkdownPreviewSession(string outputDirectory) : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetFullPath(outputDirectory), ".preview", Guid.NewGuid().ToString("N"));
    private readonly List<string> _files = [];
    private bool _disposed;
    public Uri Write(string html)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".html");
        _files.Add(path);
        File.WriteAllText(path, html, new UTF8Encoding(false));
        return new Uri(path);
    }
    public void Retain(Uri current)
    {
        foreach (var path in _files.Where(p => !string.Equals(p, current.LocalPath, StringComparison.OrdinalIgnoreCase)).ToArray()) TryRemove(path);
    }
    private void TryRemove(string path)
    {
        try { File.Delete(path); _files.Remove(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var path in _files.ToArray()) TryRemove(path);
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

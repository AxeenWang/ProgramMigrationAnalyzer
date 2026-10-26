using Microsoft.Win32;

namespace ProgramMigrationAnalyzer.App.Services;

public interface IFileDialogService
{
    string? SelectSourceFile();
    string? SelectSourceFolder();
    string? SelectMarkdownSavePath(string suggestedFileName);
    string? SelectOutputFolder(string currentDirectory) => null;
    void ShowError(string title, string message);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? SelectSourceFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "開啟原始碼",
            Filter = "Supported source (*.cs;*.4gl;*.per)|*.cs;*.4gl;*.per|C# (*.cs)|*.cs|4GL (*.4gl;*.per)|*.4gl;*.per|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectSourceFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "開啟原始碼資料夾",
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? SelectMarkdownSavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "儲存 Markdown 報告",
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
            FileName = suggestedFileName,
            AddExtension = true,
            DefaultExt = ".md"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectOutputFolder(string currentDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "設定輸出根目錄（報告依來源資料夾分開存放）",
            InitialDirectory = currentDirectory,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void ShowError(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
}

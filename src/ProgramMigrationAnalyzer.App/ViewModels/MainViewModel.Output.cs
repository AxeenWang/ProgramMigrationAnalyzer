using CommunityToolkit.Mvvm.Input;

namespace ProgramMigrationAnalyzer.App.ViewModels;

public partial class MainViewModel
{
    public string CurrentOutputDirectory => SelectedDocument is null
        ? OutputDirectory
        : _outputWriter.GetDocumentOutputDirectory(SelectedDocument.Model, SelectedDocument.SourceRootDirectory);

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void ConfigureOutputDirectory()
    {
        if (!CanOpen()) return;
        IsBusy = true;
        try
        {
            var directory = _dialogs.SelectOutputFolder(OutputDirectory);
            EnsureActive();
            if (directory is null) return;
            _outputWriter.SetOutputDirectory(directory);
            OnPropertyChanged(nameof(OutputDirectory));
            OnPropertyChanged(nameof(CurrentOutputDirectory));
            StatusMessage = "輸出根目錄已設定";
            AddLog($"Output root: {OutputDirectory}");
        }
        catch (OperationCanceledException) when (!IsWorkspaceActive) { }
        catch (Exception exception)
        {
            if (!IsWorkspaceActive) return;
            AddLog($"ERROR: {exception.Message}");
            _dialogs.ShowError("無法設定輸出目錄", exception.Message);
        }
        finally { if (IsWorkspaceActive) IsBusy = false; }
    }
}

using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ProgramMigrationAnalyzer.App.ViewModels;
namespace ProgramMigrationAnalyzer.App;
internal partial class AuthorizationActivationWindow : Window
{
    internal AuthorizationActivationViewModel ViewModel { get; }
    internal AuthorizationActivationWindow(AuthorizationActivationViewModel viewModel)
    {
        ViewModel = viewModel; InitializeComponent(); DataContext = viewModel;
        if (viewModel.IsElevatedImport) SelectKeyButton.Visibility = Visibility.Collapsed;
        Closed += (_, _) => ViewModel.Dispose();
    }
    private async void OnSelectKey(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "選擇公司簽發的授權 Key", Filter = "授權 Key|*.key;*.json|所有檔案|*.*" };
        if (dialog.ShowDialog(this) == true) await ViewModel.PreviewAsync(dialog.FileName);
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    { if (e.Key != Key.Escape) return; e.Handled = true; ViewModel.Cancel(); }
}

using System.Windows;
using System.Windows.Input;
using ProgramMigrationAnalyzer.App.ViewModels;

namespace ProgramMigrationAnalyzer.App;

internal partial class LocalAccountConfigurationWindow : Window
{
    private readonly LocalAccountConfigurationViewModel _viewModel;

    public LocalAccountConfigurationWindow(LocalAccountConfigurationViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ClearSensitiveInputsRequested += OnClearSensitiveInputs;
        Loaded += (_, _) => UsernameInput.Focus();
        Closed += OnClosed;
    }

    private async void OnSubmit(object sender, RoutedEventArgs args)
    {
        if (!_viewModel.SubmitCommand.CanExecute(null)) return;
        using var password = PasswordInput.SecurePassword;
        using var confirmation = ConfirmationInput.SecurePassword;
        using var submission = new LocalAccountSubmission(password, confirmation);
        try { await _viewModel.SubmitCommand.ExecuteAsync(submission); }
        finally { ClearPasswords(); }
    }

    private void OnClearSensitiveInputs(object? sender, EventArgs args) => ClearPasswords();
    private void ClearPasswords() { PasswordInput.Clear(); ConfirmationInput.Clear(); }
    private void OnClose(object sender, RoutedEventArgs args) => Close();
    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        args.Handled = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _viewModel.CancelPending();
        _viewModel.ClearSensitiveInputsRequested -= OnClearSensitiveInputs;
        Closed -= OnClosed;
        ClearPasswords();
    }
}

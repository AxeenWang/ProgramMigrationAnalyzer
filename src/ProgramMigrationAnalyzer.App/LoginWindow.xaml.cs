using System.Windows;
using System.Windows.Input;
using ProgramMigrationAnalyzer.App.ViewModels;

namespace ProgramMigrationAnalyzer.App;

internal partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;
    public LoginWindow(LoginViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.ClearSensitiveInputsRequested += OnClearSensitiveInputs;
        _viewModel.LoginCommand.CanExecuteChanged += OnAvailabilityChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        RefreshSubmit();
    }
    private void OnLoaded(object sender, RoutedEventArgs args) => UsernameInput.Focus();
    private void OnAvailabilityChanged(object? sender, EventArgs args) => RefreshSubmit();
    private void RefreshSubmit() => LoginButton.IsEnabled = _viewModel.LoginCommand.CanExecute(null);
    private async void OnLogin(object sender, RoutedEventArgs args)
    {
        if (!_viewModel.LoginCommand.CanExecute(null)) return;
        using var password = PasswordInput.SecurePassword;
        try { await _viewModel.LoginCommand.ExecuteAsync(password); }
        finally { PasswordInput.Clear(); }
    }
    private void OnClearSensitiveInputs(object? sender, EventArgs args) => PasswordInput.Clear();
    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        args.Handled = true;
        _viewModel.Cancel();
    }
    private void OnClosed(object? sender, EventArgs args)
    {
        _viewModel.Cancel();
        _viewModel.ClearSensitiveInputsRequested -= OnClearSensitiveInputs;
        _viewModel.LoginCommand.CanExecuteChanged -= OnAvailabilityChanged;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        PasswordInput.Clear();
        _viewModel.Dispose();
    }
}

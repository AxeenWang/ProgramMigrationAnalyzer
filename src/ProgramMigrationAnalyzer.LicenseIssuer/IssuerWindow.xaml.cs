using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProgramMigrationAnalyzer.LicenseIssuer.ViewModels;

namespace ProgramMigrationAnalyzer.LicenseIssuer;

public partial class IssuerWindow : Window
{
    private readonly IssuerViewModel _viewModel;
    public IssuerWindow() : this(new IssuerViewModel()) { }
    public IssuerWindow(IssuerViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ClearSensitiveInputsRequested += OnClearSensitiveInputs;
        Closed += OnClosed;
    }
    private async void OnSubmit(object sender, RoutedEventArgs args)
    {
        if (!_viewModel.CanEdit) return;
        using var password = PasswordInput.SecurePassword;
        using var confirmation = ConfirmationInput.SecurePassword;
        using var submission = new IssuerSecretSubmission(password,confirmation);
        try { await _viewModel.ApplyAccountAsync((IssuerAccountOperation)OperationInput.SelectedIndex,UsernameInput.Text,DisplayNameInput.Text,submission); }
        finally { ClearSecrets(); }
    }
    private async void OnGenerate(object sender, RoutedEventArgs args)
    {
        if (!_viewModel.CanEdit) return;
        try
        {
            if (!ConfirmKeyChange()) return;
            var privateDialog = new SaveFileDialog { Title = "儲存新的加密私鑰（Git 專案之外）",
                FileName = "company-signing-private-key.pem", Filter = "加密私鑰 PEM|*.pem", OverwritePrompt = false };
            if (privateDialog.ShowDialog(this) != true) return;
            if (File.Exists(privateDialog.FileName))
            {
                MessageBox.Show(this,"私鑰檔已存在，請選擇新的檔案名稱，或使用載入加密私鑰。","未覆寫私鑰");
                return;
            }
            var publicDialog = PublicDialog();
            if (publicDialog.ShowDialog(this) != true) return;
            if (Path.GetFullPath(privateDialog.FileName).Equals(Path.GetFullPath(publicDialog.FileName),StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this,"私鑰與公鑰必須使用不同檔案。","請確認儲存位置");
                return;
            }
            using var password = ProtectionInput.SecurePassword;
            using var confirmation = ProtectionConfirmationInput.SecurePassword;
            using var submission = new IssuerSecretSubmission(password,confirmation);
            await _viewModel.GenerateAndSaveKeyAsync(privateDialog.FileName,publicDialog.FileName,submission);
        }
        finally { ClearSecrets(); }
    }
    private async void OnLoad(object sender, RoutedEventArgs args)
    {
        if (!_viewModel.CanEdit) return;
        try
        {
            if (!ConfirmKeyChange()) return;
            var dialog = new OpenFileDialog { Title = "載入公司的加密私鑰", Filter = "加密私鑰 PEM|*.pem" };
            if (dialog.ShowDialog(this) != true) return;
            using var password = ProtectionInput.SecurePassword;
            using var submission = new IssuerSecretSubmission(password,password);
            await _viewModel.LoadKeyAsync(dialog.FileName,submission);
        }
        finally { ClearSecrets(); }
    }
    private async void OnExportPublic(object sender, RoutedEventArgs args)
    {
        if (!_viewModel.CanEdit) return;
        try { var dialog = PublicDialog(); if (dialog.ShowDialog(this) == true) await _viewModel.ExportPublicAsync(dialog.FileName); }
        finally { ClearSecrets(); }
    }
    private async void OnOpenAuthorization(object sender, RoutedEventArgs args)
    {
        if (!_viewModel.CanEdit) return;
        try
        {
            if (_viewModel.Accounts.Count > 0 && MessageBox.Show(this,"開啟其他授權將取代目前尚未匯出的帳號編輯，是否繼續？",
                "開啟授權",MessageBoxButton.YesNo,MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var dialog = new OpenFileDialog { Title = "開啟公司簽發的授權 Key", Filter = "授權 Key|*.pma-key;*.json|所有檔案|*.*" };
            if (dialog.ShowDialog(this) == true) await _viewModel.OpenAuthorizationAsync(dialog.FileName);
        }
        finally { ClearSecrets(); }
    }
    private async void OnExportAuthorization(object sender, RoutedEventArgs args)
    {
        if (!_viewModel.CanEdit) return;
        try
        {
            var dialog = new SaveFileDialog { Title = "簽發授權 Key", FileName = "internal-authorization.pma-key",Filter = "授權 Key|*.pma-key" };
            if (dialog.ShowDialog(this) == true) await _viewModel.ExportAuthorizationAsync(dialog.FileName);
        }
        finally { ClearSecrets(); }
    }
    private bool ConfirmKeyChange() => !_viewModel.HasSigningKey || MessageBox.Show(this,
        "更換簽章金鑰會清空目前帳號編輯，並需要使用對應公鑰重新建置客戶端。是否繼續？","更換公司金鑰",
        MessageBoxButton.YesNo,MessageBoxImage.Question) == MessageBoxResult.Yes;
    private static SaveFileDialog PublicDialog() => new() { Title = "匯出公司公鑰", FileName = "authorization-public-key.pem",Filter = "公鑰 PEM|*.pem" };
    private void OnAccountSelected(object sender, SelectionChangedEventArgs args)
    {
        if (!_viewModel.CanEdit || AccountsGrid.SelectedItem is not IssuerAccountSummary account) return;
        UsernameInput.Text = account.Username; DisplayNameInput.Text = account.DisplayName; ClearSecrets();
    }
    private void OnOperationChanged(object sender, SelectionChangedEventArgs args)
    {
        ClearSecrets();
        if (PasswordInput is not null) PasswordInput.IsEnabled = OperationInput.SelectedIndex is 0 or 1;
        if (ConfirmationInput is not null) ConfirmationInput.IsEnabled = OperationInput.SelectedIndex is 0 or 1;
    }
    private void OnClearSensitiveInputs(object? sender, EventArgs args) => ClearSecrets();
    private void ClearSecrets()
    {
        PasswordInput?.Clear(); ConfirmationInput?.Clear(); ProtectionInput?.Clear(); ProtectionConfirmationInput?.Clear();
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs args) { if (args.Key == Key.Escape) { args.Handled = true; Close(); } }
    private void OnClose(object sender, RoutedEventArgs args) => Close();
    private void OnClosed(object? sender, EventArgs args)
    {
        _viewModel.Dispose();
        _viewModel.ClearSensitiveInputsRequested -= OnClearSensitiveInputs;
        Closed -= OnClosed;
        ClearSecrets();
    }
}

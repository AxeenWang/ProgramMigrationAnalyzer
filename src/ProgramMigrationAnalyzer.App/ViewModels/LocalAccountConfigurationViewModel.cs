using System.Security;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.App.ViewModels;

internal enum LocalAccountConfigurationMode { Create, Reset, Enable, Disable }

internal sealed class LocalAccountSubmission : IDisposable
{
    private bool _disposed;
    private readonly SecureString _password;
    private readonly SecureString _confirmation;

    public LocalAccountSubmission(SecureString password, SecureString confirmation)
    {
        _password = password.Copy();
        _password.MakeReadOnly();
        try
        {
            _confirmation = confirmation.Copy();
            _confirmation.MakeReadOnly();
        }
        catch { _password.Dispose(); throw; }
    }

    public SecureString Password
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _password; }
    }

    public bool PasswordsMatch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var first = IntPtr.Zero;
        var second = IntPtr.Zero;
        try
        {
            first = Marshal.SecureStringToBSTR(_password);
            second = Marshal.SecureStringToBSTR(_confirmation);
            var difference = _password.Length ^ _confirmation.Length;
            for (var index = 0; index < Math.Min(_password.Length, _confirmation.Length); index++)
                difference |= Marshal.ReadInt16(first, index * sizeof(char)) ^ Marshal.ReadInt16(second, index * sizeof(char));
            return difference == 0;
        }
        finally
        {
            if (first != IntPtr.Zero) Marshal.ZeroFreeBSTR(first);
            if (second != IntPtr.Zero) Marshal.ZeroFreeBSTR(second);
        }
    }

    public override string ToString() => nameof(LocalAccountSubmission);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _password.Dispose();
        _confirmation.Dispose();
    }
}

internal sealed partial class LocalAccountConfigurationViewModel : ObservableObject
{
    private readonly LocalAccountAdministrationService _administration;
    private readonly bool _canManage;
    private bool _closed;

    public LocalAccountConfigurationViewModel(LocalAccountAdministrationService administration)
    {
        _administration = administration;
        _canManage = administration.IsElevatedAdministrator;
        SubmitCommand = new AsyncRelayCommand<LocalAccountSubmission>(SubmitAsync, _ => CanEdit);
        StatusMessage = _canManage ? "請選擇操作並輸入帳號。此視窗不會登入分析工具。"
            : "請由 Windows 管理者以提升權限的程序設定本機帳號。";
    }

    public IReadOnlyList<LocalAccountModeOption> ModeOptions { get; } =
    [
        new(LocalAccountConfigurationMode.Create, "建立帳號"),
        new(LocalAccountConfigurationMode.Reset, "重設密碼"),
        new(LocalAccountConfigurationMode.Enable, "啟用帳號"),
        new(LocalAccountConfigurationMode.Disable, "停用帳號")
    ];

    [ObservableProperty] private string username = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private LocalAccountConfigurationMode mode;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private bool isBusy;

    public bool CanEdit => _canManage && !_closed && !IsBusy;
    public bool NeedsPassword => Mode is LocalAccountConfigurationMode.Create or LocalAccountConfigurationMode.Reset;
    public bool CanEditPassword => CanEdit && NeedsPassword;
    public IAsyncRelayCommand<LocalAccountSubmission> SubmitCommand { get; }
    public event EventHandler? ClearSensitiveInputsRequested;

    partial void OnModeChanged(LocalAccountConfigurationMode value)
    {
        OnPropertyChanged(nameof(NeedsPassword));
        OnPropertyChanged(nameof(CanEditPassword));
        ClearSensitiveInputsRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditPassword));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    private async Task SubmitAsync(LocalAccountSubmission? submission, CancellationToken cancellationToken)
    {
        if (!CanEdit) return;
        var selectedMode = Mode;
        var selectedUsername = Username;
        var selectedDisplayName = DisplayName;
        IsBusy = true;
        ClearSensitiveInputsRequested?.Invoke(this, EventArgs.Empty);
        try
        {
            if (NeedsPassword)
            {
                if (submission is null || !submission.PasswordsMatch())
                {
                    StatusMessage = "兩次密碼必須一致。";
                    return;
                }
                // Validate before any filesystem initialization. Hashing itself runs in the service's background task.
                LocalAccountValidation.ValidateNewPassword(submission.Password);
                StatusMessage = "正在儲存帳號設定…";
                if (selectedMode == LocalAccountConfigurationMode.Create)
                    await _administration.CreateAsync(selectedUsername, selectedDisplayName, submission.Password, cancellationToken);
                else
                    await _administration.ResetAsync(selectedUsername, selectedDisplayName, submission.Password, cancellationToken);
            }
            else if (selectedMode is LocalAccountConfigurationMode.Enable or LocalAccountConfigurationMode.Disable)
                await _administration.SetEnabledAsync(selectedUsername, selectedMode == LocalAccountConfigurationMode.Enable, cancellationToken);
            else throw new ArgumentException("Unsupported account operation.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!_closed) StatusMessage = "帳號設定已儲存，下次登入生效。";
        }
        catch (OperationCanceledException)
        { if (!_closed) StatusMessage = "設定已取消。"; }
        catch (UnauthorizedAccessException)
        { if (!_closed) StatusMessage = "需要提升權限的 Windows 管理者程序。"; }
        catch (ArgumentException)
        { if (!_closed) StatusMessage = "請確認帳號、顯示名稱、操作模式及密碼，密碼須為 15～128 個 Unicode 字元。"; }
        catch (LocalAccountConfigurationException exception)
        {
            if (!_closed) StatusMessage = exception.Failure == LocalAccountConfigurationFailure.Busy
                ? "其他管理程序正在更新，請稍後重試。"
                : "本機帳號設定無法安全讀取或儲存，請聯絡管理者。";
        }
        finally
        {
            IsBusy = false;
            ClearSensitiveInputsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CancelPending()
    {
        _closed = true;
        SubmitCommand.Cancel();
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditPassword));
        SubmitCommand.NotifyCanExecuteChanged();
        ClearSensitiveInputsRequested?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed record LocalAccountModeOption(LocalAccountConfigurationMode Mode, string Label);

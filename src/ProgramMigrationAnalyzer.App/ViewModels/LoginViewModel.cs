using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.Core.Authentication;

namespace ProgramMigrationAnalyzer.App.ViewModels;

internal sealed partial class LoginViewModel : ObservableObject, IDisposable
{
    private readonly IAuthenticationService _authentication;
    private readonly TimeProvider _timeProvider;
    private readonly AuthenticationDiagnosticLog _diagnostics;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private CancellationTokenSource? _requestCancellation;
    private ITimer? _countdown;
    private long _generation;
    private long? _cooldownStarted;
    private int _failures;
    private bool _closed;
    private bool _completed;

    public LoginViewModel(IAuthenticationService authentication, TimeProvider timeProvider, AuthenticationDiagnosticLog diagnostics)
    {
        _authentication = authentication;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        LoginCommand = new AsyncRelayCommand<SecureString>(LoginAsync, _ => CanSubmit);
        CancelLoginCommand = new RelayCommand(Cancel, () => !_closed);
    }

    [ObservableProperty] private string username = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "請輸入帳號與密碼。";
    [ObservableProperty] private int remainingCooldownSeconds;
    public bool CanEdit => !_closed && !_completed && !IsBusy;
    private bool CanSubmit => CanEdit && RemainingCooldownSeconds == 0;
    public IAsyncRelayCommand<SecureString> LoginCommand { get; }
    public IRelayCommand CancelLoginCommand { get; }
    public event Action<AuthenticatedUser>? LoginSucceeded;
    public event Action? Cancelled;
    public event EventHandler? ClearSensitiveInputsRequested;

    partial void OnIsBusyChanged(bool value) => NotifyAvailability();
    partial void OnRemainingCooldownSecondsChanged(int value) => LoginCommand.NotifyCanExecuteChanged();
    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanEdit));
        LoginCommand.NotifyCanExecuteChanged();
        CancelLoginCommand.NotifyCanExecuteChanged();
    }

    internal async Task LoginAsync(SecureString? password, CancellationToken cancellationToken)
    {
        RefreshCooldown();
        if (!CanSubmit) return;
        if (string.IsNullOrWhiteSpace(Username) || password is null || password.Length == 0)
        {
            StatusMessage = "請輸入帳號與密碼。";
            ClearSensitiveInputsRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var generation = ++_generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _requestCancellation = cancellation;
        using var request = new LoginRequest(LoginRequestKind.Credentials, Username, password);
        IsBusy = true;
        StatusMessage = "登入中…";
        ClearSensitiveInputsRequested?.Invoke(this, EventArgs.Empty);
        try
        {
            AuthenticationResult result;
            try { result = await _authentication.AuthenticateAsync(request, cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            catch (Exception) { result = AuthenticationResult.Failed(AuthenticationFailure.UnexpectedFailure); }
            if (_closed || generation != _generation || cancellation.IsCancellationRequested) return;
            if (result.IsSuccess)
            {
                _failures = 0;
                _completed = true;
                NotifyAvailability();
                _diagnostics.Record(AuthenticationDiagnosticKind.Succeeded, result.User!.UserId);
                LoginSucceeded?.Invoke(result.User);
            }
            else
            {
                _diagnostics.Record(AuthenticationDiagnosticKind.Failed, failure: result.Failure);
                StatusMessage = result.Failure switch
                {
                    AuthenticationFailure.InvalidCredentials => "帳號或密碼不正確，或帳號無法使用",
                    AuthenticationFailure.ConfigurationInvalid => "本機登入尚未正確設定，請聯絡管理者",
                    _ => "登入暫時無法完成，請稍後重試。"
                };
                if (++_failures >= 5)
                {
                    _cooldownStarted = _timeProvider.GetTimestamp();
                    RefreshCooldown();
                    _countdown = _timeProvider.CreateTimer(_ => PostRefresh(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_requestCancellation, cancellation)) _requestCancellation = null;
            IsBusy = false;
            ClearSensitiveInputsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PostRefresh()
    {
        if (_context is null) RefreshCooldown();
        else _context.Post(_ => RefreshCooldown(), null);
    }

    private void RefreshCooldown()
    {
        if (_closed || _cooldownStarted is not { } started) return;
        RemainingCooldownSeconds = Math.Max(0, (int)Math.Ceiling(30 - _timeProvider.GetElapsedTime(started).TotalSeconds));
        if (RemainingCooldownSeconds > 0)
            StatusMessage = $"請稍後再試，剩餘 {RemainingCooldownSeconds} 秒。";
        else
        {
            _cooldownStarted = null;
            _failures = 0;
            _countdown?.Dispose();
            _countdown = null;
            StatusMessage = "請重新輸入帳號與密碼。";
        }
    }

    internal void ReportInitializationFailure()
    {
        if (_closed) return;
        _completed = false;
        StatusMessage = "主畫面無法初始化，請重新登入。";
        NotifyAvailability();
    }

    public void Cancel()
    {
        if (_closed) return;
        Invalidate();
        _diagnostics.Record(AuthenticationDiagnosticKind.Cancelled);
        Cancelled?.Invoke();
    }

    private void Invalidate()
    {
        _closed = true;
        ++_generation;
        _requestCancellation?.Cancel();
        LoginCommand.Cancel();
        _countdown?.Dispose();
        _countdown = null;
        NotifyAvailability();
        ClearSensitiveInputsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (!_closed) Invalidate();
        LoginSucceeded = null;
        Cancelled = null;
        ClearSensitiveInputsRequested = null;
    }
}

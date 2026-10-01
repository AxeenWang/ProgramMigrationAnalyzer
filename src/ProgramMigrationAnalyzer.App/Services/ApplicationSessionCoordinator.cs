using System.Windows;
using System.Windows.Controls;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core.Authentication;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.App.Services;

public enum ApplicationSessionState { Starting, InspectingAuthorization, Activation, ImportingAuthorization, Login, OpeningMain, Main, ReturningToLogin, Closing }

public sealed class ApplicationSessionCoordinator : IDisposable
{
    private readonly Application _app;
    private readonly IAuthenticationService _authentication;
    private readonly Func<IUserSession, MainWindow> _mainWindowFactory;
    private readonly SignedLocalAccountStore _store;
    private readonly Func<bool, AuthorizationActivationWindow> _activationWindowFactory;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeProvider _timeProvider;
    private readonly AuthenticationDiagnosticLog _diagnostics;
    private readonly AuthenticatedSession _session = new();
    private LoginWindow? _login;
    private LoginViewModel? _loginViewModel;
    private MainWindow? _main;
    private Window? _error;
    private AuthorizationActivationWindow? _activation;
    private bool _importMode;
    private bool _disposed;
    private bool _shutdownRequested;

    internal ApplicationSessionCoordinator(Application app, IAuthenticationService authentication,
        Func<IUserSession, MainWindow> mainWindowFactory, SignedLocalAccountStore store, Func<bool, AuthorizationActivationWindow> activationWindowFactory,
        TimeProvider timeProvider, AuthenticationDiagnosticLog diagnostics)
    {
        _app = app;
        _authentication = authentication;
        _mainWindowFactory = mainWindowFactory;
        _store = store;
        _activationWindowFactory = activationWindowFactory;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
    }
    public IUserSession Session => _session;
    public ApplicationSessionState State { get; private set; } = ApplicationSessionState.Starting;

    internal async Task StartAsync(AuthorizationStartupRequest request)
    {
        _app.Dispatcher.VerifyAccess();
        if (_disposed || State != ApplicationSessionState.Starting) throw new InvalidOperationException("Startup has already been handled.");
        _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _importMode = request.Mode == AuthorizationStartupMode.ImportAuthorization;
        if (_importMode)
        {
            ShowActivation();
            await _activation!.ViewModel.PreviewAsync(request.AbsolutePath!, _lifetime.Token);
            return;
        }
        State = ApplicationSessionState.InspectingAuthorization;
        try { await _store.ReadAsync(_lifetime.Token); }
        catch (AuthorizationException) { if (!_disposed) ShowActivation(); return; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        if (!_disposed) ShowLogin();
    }
    private void ShowActivation()
    {
        CloseLogin();
        State = _importMode ? ApplicationSessionState.ImportingAuthorization : ApplicationSessionState.Activation;
        _activation = _activationWindowFactory(_importMode);
        _activation.ViewModel.Activated += OnActivated;
        _activation.ViewModel.Cancelled += OnActivationCancelled;
        _activation.Closed += OnActivationClosed;
        _app.MainWindow = _activation;
        _activation.Show();
    }
    private void OnActivated()
    {
        if (_disposed || _activation is not { IsVisible: true }
            || State is not (ApplicationSessionState.Activation or ApplicationSessionState.ImportingAuthorization)) return;
        if (_importMode) { Shutdown(0); return; }
        CloseActivation(); ShowLogin();
    }
    private void RequestAuthorizationUpdate()
    {
        if (!_disposed && State == ApplicationSessionState.Login && _loginViewModel is { CanEdit: true }) ShowActivation();
    }
    private void OnActivationCancelled() => Shutdown(_importMode ? 2 : 0);
    private void OnActivationClosed(object? sender, EventArgs args) => OnActivationCancelled();
    private void CloseActivation()
    {
        var window = _activation; _activation = null;
        if (window is null) return;
        window.ViewModel.Activated -= OnActivated; window.ViewModel.Cancelled -= OnActivationCancelled;
        window.Closed -= OnActivationClosed; window.ViewModel.Dispose(); window.Close();
    }
    internal void RejectStartup()
    {
        State = ApplicationSessionState.Closing;
        _session.Clear();
        CloseLogin();
        CloseMain();
        CloseActivation();
        if (_error is { } previous)
        {
            previous.Closed -= OnErrorClosed;
            previous.Close();
        }
        _diagnostics.Record(AuthenticationDiagnosticKind.InitializationFailed);
        var message = new TextBlock { Text = "程式無法啟動，請重新開啟或聯絡管理者。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
        _error = new Window { Title = "程式移植分析儀", Width = 440, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = message };
        _error.Closed += OnErrorClosed;
        _app.MainWindow = _error;
        _error.Show();
    }
    private void ShowLogin()
    {
        State = ApplicationSessionState.Login;
        _loginViewModel = new(_authentication, _timeProvider, _diagnostics);
        _login = new(_loginViewModel);
        _loginViewModel.LoginSucceeded += OnLoginSucceeded;
        _loginViewModel.Cancelled += OnLoginCancelled;
        _loginViewModel.ImportAuthorizationRequested += RequestAuthorizationUpdate;
        _login.Closed += OnLoginClosed;
        _app.MainWindow = _login;
        _login.Show();
    }
    private void OnLoginSucceeded(AuthenticatedUser user)
    {
        if (_disposed || State != ApplicationSessionState.Login || _login is not { IsVisible: true }) return;
        State = ApplicationSessionState.OpeningMain;
        var login = _login;
        var loginViewModel = _loginViewModel!;
        MainWindow? candidate = null;
        try
        {
            _session.SetAuthenticated(user);
            var generation = _session.Generation;
            if (State != ApplicationSessionState.OpeningMain) return;
            candidate = _mainWindowFactory(_session);
            if (_disposed || State != ApplicationSessionState.OpeningMain || generation != _session.Generation)
            {
                candidate.Close();
                return;
            }
            _main = candidate;
            _main.Closed += OnMainClosed;
            _app.MainWindow = candidate;
            candidate.Show();
            if (State != ApplicationSessionState.OpeningMain) return;
            State = ApplicationSessionState.Main;
            CloseLogin();
        }
        catch (Exception)
        {
            if (candidate is not null)
            {
                candidate.Closed -= OnMainClosed;
                candidate.Close();
            }
            _main = null;
            _session.Clear();
            if (_disposed || State == ApplicationSessionState.Closing) return;
            State = ApplicationSessionState.Login;
            _app.MainWindow = login;
            loginViewModel.ReportInitializationFailure();
            _diagnostics.Record(AuthenticationDiagnosticKind.InitializationFailed);
        }
    }
    public void RequestLogout()
    {
        _app.Dispatcher.VerifyAccess();
        if (_disposed || State != ApplicationSessionState.Main || _main?.DataContext is MainViewModel { IsBusy: true }) return;
        State = ApplicationSessionState.ReturningToLogin;
        _diagnostics.Record(AuthenticationDiagnosticKind.LoggedOut, _session.CurrentUser?.UserId);
        _session.Clear();
        CloseMain();
        ShowLogin();
    }
    private void OnLoginClosed(object? sender, EventArgs args)
    {
        if (State is ApplicationSessionState.Login or ApplicationSessionState.OpeningMain) Shutdown();
    }
    private void OnMainClosed(object? sender, EventArgs args)
    {
        if (State is ApplicationSessionState.Main or ApplicationSessionState.OpeningMain) Shutdown();
    }
    private void OnLoginCancelled() => Shutdown();
    private void OnErrorClosed(object? sender, EventArgs args) => Shutdown(1);
    private void CloseLogin()
    {
        var window = _login;
        var vm = _loginViewModel;
        _login = null;
        _loginViewModel = null;
        if (vm is not null)
        {
            vm.LoginSucceeded -= OnLoginSucceeded;
            vm.Cancelled -= OnLoginCancelled;
            vm.ImportAuthorizationRequested -= RequestAuthorizationUpdate;
            vm.Dispose();
        }
        if (window is not null)
        {
            window.Closed -= OnLoginClosed;
            window.Close();
        }
    }
    private void CloseMain()
    {
        var window = _main;
        _main = null;
        if (window is null) return;
        window.Closed -= OnMainClosed;
        window.Dispose();
        window.Close();
    }
    public void Shutdown() => Shutdown(0);
    private void Shutdown(int exitCode)
    {
        _app.Dispatcher.VerifyAccess();
        if (_shutdownRequested) return;
        _shutdownRequested = true;
        Dispose();
        _app.Shutdown(exitCode);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        State = ApplicationSessionState.Closing;
        _session.Clear();
        CloseLogin();
        CloseMain();
        CloseActivation();
        if (_error is { } window)
        {
            _error = null;
            window.Closed -= OnErrorClosed;
            window.Close();
        }
    }
}

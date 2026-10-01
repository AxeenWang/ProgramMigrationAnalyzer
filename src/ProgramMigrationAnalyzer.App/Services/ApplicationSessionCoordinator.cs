using System.Windows;
using System.Windows.Controls;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core.Authentication;

namespace ProgramMigrationAnalyzer.App.Services;

public enum StartupMode { Normal, ConfigureLocalAccount }
public enum ApplicationSessionState { Starting, Login, OpeningMain, Main, ReturningToLogin, Closing, Admin }

internal static class StartupModeParser
{
    public static StartupMode Parse(string[] args) => args switch
    {
        [] => StartupMode.Normal,
        ["--configure-local-account"] => StartupMode.ConfigureLocalAccount,
        _ => throw new ArgumentException("Unsupported startup arguments.")
    };
}

public sealed class ApplicationSessionCoordinator : IDisposable
{
    private readonly Application _app;
    private readonly IAuthenticationService _authentication;
    private readonly Func<IUserSession, MainWindow> _mainWindowFactory;
    private readonly Func<LocalAccountConfigurationWindow> _configurationWindowFactory;
    private readonly TimeProvider _timeProvider;
    private readonly AuthenticationDiagnosticLog _diagnostics;
    private readonly AuthenticatedSession _session = new();
    private LoginWindow? _login;
    private LoginViewModel? _loginViewModel;
    private MainWindow? _main;
    private Window? _adminOrError;
    private bool _disposed;
    private bool _shutdownRequested;

    internal ApplicationSessionCoordinator(Application app, IAuthenticationService authentication,
        Func<IUserSession, MainWindow> mainWindowFactory, Func<LocalAccountConfigurationWindow> configurationWindowFactory,
        TimeProvider timeProvider, AuthenticationDiagnosticLog diagnostics)
    {
        _app = app;
        _authentication = authentication;
        _mainWindowFactory = mainWindowFactory;
        _configurationWindowFactory = configurationWindowFactory;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
    }
    public IUserSession Session => _session;
    public ApplicationSessionState State { get; private set; } = ApplicationSessionState.Starting;

    public void Start(StartupMode mode)
    {
        _app.Dispatcher.VerifyAccess();
        if (_disposed || State != ApplicationSessionState.Starting) throw new InvalidOperationException("Startup has already been handled.");
        _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (mode == StartupMode.Normal) ShowLogin();
        else if (mode == StartupMode.ConfigureLocalAccount)
        {
            State = ApplicationSessionState.Admin;
            // The administration VM and service check the elevated token before enabling any writes.
            _adminOrError = _configurationWindowFactory();
            _adminOrError.Closed += OnAdminOrErrorClosed;
            _app.MainWindow = _adminOrError;
            _adminOrError.Show();
        }
        else throw new ArgumentException("Unsupported startup mode.");
    }
    internal void RejectStartup()
    {
        var message = new TextBlock { Text = "無法以此啟動方式開啟程式，請正常啟動或聯絡管理者。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
        _adminOrError = new Window { Title = "程式移植分析儀", Width = 440, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = message };
        _adminOrError.Closed += OnAdminOrErrorClosed;
        _app.MainWindow = _adminOrError;
        _adminOrError.Show();
    }
    private void ShowLogin()
    {
        State = ApplicationSessionState.Login;
        _loginViewModel = new(_authentication, _timeProvider, _diagnostics);
        _login = new(_loginViewModel);
        _loginViewModel.LoginSucceeded += OnLoginSucceeded;
        _loginViewModel.Cancelled += Shutdown;
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
    private void OnAdminOrErrorClosed(object? sender, EventArgs args) => Shutdown();
    private void CloseLogin()
    {
        var window = _login;
        var vm = _loginViewModel;
        _login = null;
        _loginViewModel = null;
        if (vm is not null)
        {
            vm.LoginSucceeded -= OnLoginSucceeded;
            vm.Cancelled -= Shutdown;
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
    public void Shutdown()
    {
        _app.Dispatcher.VerifyAccess();
        if (_shutdownRequested) return;
        _shutdownRequested = true;
        Dispose();
        _app.Shutdown();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        State = ApplicationSessionState.Closing;
        _session.Clear();
        CloseLogin();
        CloseMain();
        if (_adminOrError is { } window)
        {
            _adminOrError = null;
            window.Closed -= OnAdminOrErrorClosed;
            window.Close();
        }
    }
}

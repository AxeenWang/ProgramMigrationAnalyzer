using System.IO;
using System.Windows;
using System.Windows.Threading;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class StartupChecks
{
    public static void Run() => CheckSupport.Run(
        ("StartupCreatesOnlyLogin", () => RunStaChild("", "normal")),
        ("SwitchAndExitLifetime", () => RunStaChild("", "retry")),
        ("CancelAndLateSuccess", () => RunStaChild("", "cancel")),
        ("ExistingMainCannotBypass", () => RunStaChild("", "existing")),
        ("NonElevatedAdministration", () => RunStaChild("--configure-local-account", "admin-denied")),
        ("AdministrationExitIsIsolated", () => RunStaChild("--configure-local-account", "admin")),
        ("AdministrationInitializationFailsClosed", () => RunStaChild("--configure-local-account", "admin-error")),
        ("OpeningMainCancellationDisposesCandidate", () => RunStaChild("", "opening-cancel")),
        ("CountdownUsesUiDispatcher", () => RunStaChild("", "throttle")),
        ("StrictStartupModes", StrictStartupModes),
        ("InvalidModeExits", () => RunStaChild("--skip-login", "invalid")));

    private static void StrictStartupModes()
    {
        Check(StartupModeParser.Parse([]) == StartupMode.Normal
            && StartupModeParser.Parse(["--configure-local-account"]) == StartupMode.ConfigureLocalAccount,
            "Only normal or the unique admin argument must be accepted.");
        foreach (var arguments in new[] { new[] { "--skip-login" }, new[] { "--configure-local-account", "extra" },
            new[] { "--CONFIGURE-LOCAL-ACCOUNT" }, new[] { "--configure-local-account", "--configure-local-account" } })
            Throws<ArgumentException>(() => StartupModeParser.Parse(arguments));
    }

    internal static int RunChild(string scenario)
    {
        using var fixture = new AccountFixture();
        fixture.Policy.IsElevatedAdministrator = scenario != "admin-denied";
        var service = new FakeAuthentication { Handler = (_, _) => Task.FromResult(AuthenticationResult.Succeeded(LoginChecks.User)) };
        var time = new ManualTimeProvider();
        AuthenticationTestApp? app = null;
        app = new AuthenticationTestApp(service, session => {
            Check(session.IsAuthenticated, "Factory can only receive an authenticated session.");
            var candidate = new MainWindowFactory(() => app!.Coordinator!.RequestLogout(), Path.Combine(fixture.DirectoryPath, "output")).Create(session);
            if (scenario == "opening-cancel") app!.Windows.OfType<LoginWindow>().Single().Close();
            return candidate;
        }, () => scenario == "admin-error" ? throw new InvalidOperationException("test-only secret startup failure")
            : new LocalAccountConfigurationWindow(new LocalAccountConfigurationViewModel(
                new ProgramMigrationAnalyzer.Infrastructure.Authentication.LocalAccountAdministrationService(fixture.Store, new(), fixture.Policy))), time);
        app.FailFirstMain = scenario == "retry";
        app.Resources = (ResourceDictionary)Application.LoadComponent(
            new Uri("/ProgramMigrationAnalyzer.App;component/Resources/ApplicationResources.xaml", UriKind.Relative));
        var exited = false;
        app.Exit += (_, _) => exited = true;
        Window? placeholder = null;
        if (scenario == "existing") app.MainWindow = placeholder = new Window();
        var result = 0;
        app.DispatcherUnhandledException += (_, args) => {
            Console.Error.WriteLine(args.Exception); result = 1; args.Handled = true; app.Shutdown();
        };
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            try
            {
                var coordinator = app.Coordinator!;
                if (scenario == "admin-error")
                {
                    Check(app.MainCalls == 0 && !coordinator.Session.IsAuthenticated && service.Calls == 0,
                        "Failed administration initialization must not create a session or workspace.");
                    var error = app.Windows.OfType<Window>().Single();
                    Check(error.Content is System.Windows.Controls.TextBlock text && !text.Text.Contains("secret"),
                        "Startup failure must show a safe error without exposing exception content.");
                    error.Close();
                    return;
                }
                if (scenario == "invalid")
                {
                    Check(!coordinator.Session.IsAuthenticated && app.MainCalls == 0, "Invalid mode must never create a session or workspace.");
                    app.Windows.OfType<Window>().Single().Close();
                    return;
                }
                if (scenario.StartsWith("admin"))
                {
                    Check(coordinator.State == ApplicationSessionState.Admin && app.MainCalls == 0
                        && !coordinator.Session.IsAuthenticated && service.Calls == 0, "Admin mode must not authenticate or create a workspace.");
                    var configuration = app.Windows.OfType<LocalAccountConfigurationWindow>().Single();
                    var vm = (LocalAccountConfigurationViewModel)configuration.DataContext;
                    Check(vm.CanEdit == (scenario == "admin"), "Non-elevated administration must be denied.");
                    Check(!Directory.Exists(fixture.DirectoryPath), "Opening admin mode cannot initialize account storage.");
                    configuration.Close();
                    return;
                }
                Check(app.ShutdownMode == ShutdownMode.OnExplicitShutdown && coordinator.State == ApplicationSessionState.Login
                    && app.MainCalls == 0 && !coordinator.Session.IsAuthenticated, "Normal startup must create only login and no authenticated session.");
                Check(!Directory.Exists(Path.Combine(fixture.DirectoryPath, "output")), "Startup must not create output.");
                var login = app.Windows.OfType<LoginWindow>().Single();
                var loginVm = (LoginViewModel)login.DataContext;
                CaptureLogin(login, scenario);
                loginVm.Username = "test.user";
                using var password = Password("Test-only startup password");
                if (scenario == "throttle")
                {
                    service.Handler = (_, _) => Task.FromResult(AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials));
                    for (var i = 0; i < 5; i++) await loginVm.LoginCommand.ExecuteAsync(password);
                    Check(loginVm.RemainingCooldownSeconds == 30, "UI must begin its cooldown at the fifth failure.");
                    await Task.Run(() => time.Advance(TimeSpan.FromSeconds(29)));
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Check(loginVm.RemainingCooldownSeconds == 1
                        && !((System.Windows.Controls.Button)login.FindName("LoginButton")).IsEnabled, "Timer must update the UI through its dispatcher.");
                    await Task.Run(() => time.Advance(TimeSpan.FromSeconds(1)));
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Check(((System.Windows.Controls.Button)login.FindName("LoginButton")).IsEnabled, "Timer expiration must restore the UI button.");
                    loginVm.Cancel();
                    return;
                }
                if (scenario == "cancel")
                {
                    var pending = new TaskCompletionSource<AuthenticationResult>();
                    service.Handler = (_, _) => pending.Task;
                    var task = loginVm.LoginCommand.ExecuteAsync(password);
                    login.Close();
                    pending.SetResult(AuthenticationResult.Succeeded(LoginChecks.User));
                    await task;
                    Check(app.MainCalls == 0 && !coordinator.Session.IsAuthenticated, "Late success after closing login must be ignored.");
                    return;
                }
                await loginVm.LoginCommand.ExecuteAsync(password);
                if (scenario == "opening-cancel")
                {
                    Check(coordinator.State == ApplicationSessionState.Closing && !coordinator.Session.IsAuthenticated
                        && app.Windows.OfType<MainWindow>().Count() == 0, "Cancel during factory creation must dispose the unshown candidate and exit.");
                    return;
                }
                if (scenario == "retry")
                {
                    Check(coordinator.State == ApplicationSessionState.Login && !coordinator.Session.IsAuthenticated
                        && login.IsVisible && loginVm.CanEdit, "Failed initialization must clear session and permit retry.");
                    await loginVm.LoginCommand.ExecuteAsync(password);
                }
                Check(coordinator.State == ApplicationSessionState.Main && coordinator.Session.IsAuthenticated
                    && app.MainCalls == (scenario == "retry" ? 2 : 1) && !exited, "Success must switch once without exiting.");
                Check(app.Windows.OfType<LoginWindow>().Count() == 0 && app.Windows.OfType<MainWindow>().Count() == 1,
                    "Switch must close login and leave exactly one analyzer.");
                app.MainWindow.Close();
                Check(!coordinator.Session.IsAuthenticated, "Closing main must clear session.");
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; app.Coordinator?.Shutdown(); }
        }));
        app.Run();
        try
        {
            Check(exited && app.MainCalls == (scenario is "admin" or "admin-denied" or "admin-error" or "cancel" or "invalid" or "throttle" ? 0 : scenario == "retry" ? 2 : 1),
                "Every scenario must terminate without creating an unauthorized workspace.");
            Check(app.Coordinator is { Session.IsAuthenticated: false }, "Exit must leave no session.");
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
        placeholder?.Close();
        return result;
    }

    private static void CaptureLogin(Window window, string scenario)
    {
        if (scenario != "normal") return;
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(root, ".codex-tmp", "2026-10-01_login-phase-3");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, "login.png"));
        encoder.Save(stream);
    }
}

internal sealed class AuthenticationTestApp(IAuthenticationService authentication, Func<IUserSession, MainWindow> mainFactory,
    Func<LocalAccountConfigurationWindow> adminFactory, TimeProvider? timeProvider = null) : ProgramMigrationAnalyzer.App.App
{
    public ApplicationSessionCoordinator? Coordinator { get; private set; }
    public int MainCalls { get; private set; }
    public bool FailFirstMain { get; set; }
    protected override ApplicationSessionCoordinator CreateCoordinator() => Coordinator = new(this, authentication, session => {
        MainCalls++;
        if (FailFirstMain && MainCalls == 1) throw new InvalidOperationException("test-only initialization failure");
        return mainFactory(session);
    }, adminFactory, timeProvider ?? TimeProvider.System, new AuthenticationDiagnosticLog());
}

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
        AuthenticationTestApp? app = null;
        app = new AuthenticationTestApp(service, session => {
            Check(session.IsAuthenticated, "Factory can only receive an authenticated session.");
            return new MainWindowFactory(() => app!.Coordinator!.RequestLogout(), Path.Combine(fixture.DirectoryPath, "output")).Create(session);
        }, () => new LocalAccountConfigurationWindow(new LocalAccountConfigurationViewModel(
            new ProgramMigrationAnalyzer.Infrastructure.Authentication.LocalAccountAdministrationService(fixture.Store, new(), fixture.Policy))));
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
                loginVm.Username = "test.user";
                using var password = Password("Test-only startup password");
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
            Check(exited && app.MainCalls == (scenario is "admin" or "admin-denied" or "cancel" or "invalid" ? 0 : scenario == "retry" ? 2 : 1),
                "Every scenario must terminate without creating an unauthorized workspace.");
            Check(app.Coordinator is { Session.IsAuthenticated: false }, "Exit must leave no session.");
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
        placeholder?.Close();
        return result;
    }
}

internal sealed class AuthenticationTestApp(IAuthenticationService authentication, Func<IUserSession, MainWindow> mainFactory,
    Func<LocalAccountConfigurationWindow> adminFactory) : ProgramMigrationAnalyzer.App.App
{
    public ApplicationSessionCoordinator? Coordinator { get; private set; }
    public int MainCalls { get; private set; }
    public bool FailFirstMain { get; set; }
    protected override ApplicationSessionCoordinator CreateCoordinator() => Coordinator = new(this, authentication, session => {
        MainCalls++;
        if (FailFirstMain && MainCalls == 1) throw new InvalidOperationException("test-only initialization failure");
        return mainFactory(session);
    }, adminFactory, TimeProvider.System, new AuthenticationDiagnosticLog());
}

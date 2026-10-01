using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

// Real store, PBKDF2, authentication, windows and App.OnStartup. Only the deployment ACL
// boundary is substituted, so these checks never access production ProgramData.
internal static class LocalStartupChecks
{
    private const string OriginalPassword = "  Test-only 密碼😀 credential  ";
    private const string ResetPassword = "  Reset test-only 密碼😀 credential  ";

    internal static int RunChild(bool configuration)
    {
        using var fixture = new AccountFixture("2026-10-01_login-phase-4");
        if (!configuration)
            fixture.Write(new(1, [StoreChecks.Account(), StoreChecks.Account("user.two"),
                StoreChecks.Account("user.disabled", enabled: false)])).GetAwaiter().GetResult();
        AuthenticationTestApp? app = null;
        var output = Path.Combine(fixture.DirectoryPath, "output");
        app = new AuthenticationTestApp(fixture.Authentication,
            session => new MainWindowFactory(() => app!.Coordinator!.RequestLogout(), output).Create(session),
            () => throw new InvalidOperationException("Normal startup cannot enter administration."));
        app.Resources = (ResourceDictionary)Application.LoadComponent(
            new Uri("/ProgramMigrationAnalyzer.App;component/Resources/ApplicationResources.xaml", UriKind.Relative));
        var result = 0;
        var exited = false;
        app.Exit += (_, _) => exited = true;
        app.DispatcherUnhandledException += (_, args) => {
            Console.Error.WriteLine(args.Exception); result = 1; args.Handled = true; app.Coordinator?.Shutdown();
        };
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            try
            {
                AssertLoginOnly(app);
                Check(!Directory.Exists(output), "Real provider startup cannot create output before authentication.");
                if (configuration) await ConfigurationFailures(app, fixture);
                else await Lifecycle(app, fixture, output);
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
            finally { app.Coordinator?.Shutdown(); }
        }));
        app.Run(); // Inherits production OnStartup and parses the actual empty command line.
        Check(exited && app.Coordinator is { Session.IsAuthenticated: false, State: ApplicationSessionState.Closing }
            && app.Windows.Count == 0, "Real provider scenarios must exit with no session or remaining windows.");
        if (result == 0)
            Console.WriteLine(configuration ? "Local provider configuration checked." : "Local provider lifecycle checked.");
        return result;
    }

    private static void AssertLoginOnly(AuthenticationTestApp app)
    {
        Check(app.Coordinator is { Session.IsAuthenticated: false, State: ApplicationSessionState.Login }
            && app.Windows.OfType<LoginWindow>().Count() == 1 && !app.Windows.OfType<MainWindow>().Any(),
            "Unauthenticated real provider must expose only the login window.");
    }

    private static async Task<string> Submit(AuthenticationTestApp app, string username, string password)
    {
        var window = app.Windows.OfType<LoginWindow>().Single();
        var vm = (LoginViewModel)window.DataContext;
        ((TextBox)window.FindName("UsernameInput")).Text = username;
        var input = (PasswordBox)window.FindName("PasswordInput");
        input.Password = password;
        ((Button)window.FindName("LoginButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await vm.LoginCommand.ExecutionTask!;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(input.Password.Length == 0, "The real provider UI path must clear PasswordBox on every outcome.");
        return vm.StatusMessage;
    }

    private static async Task Lifecycle(AuthenticationTestApp app, AccountFixture fixture, string output)
    {
        var before = await File.ReadAllBytesAsync(fixture.FilePath);
        string? failure = null;
        foreach (var (username, password) in new[] { ("unknown", OriginalPassword),
            ("user.one", "Incorrect test-only credential"), ("user.disabled", OriginalPassword) })
        {
            var status = await Submit(app, username, password);
            failure ??= status;
            Check(status == failure && status.Contains("帳號或密碼不正確"),
                "Unknown, wrong-password and disabled accounts must have the same UI failure.");
            AssertLoginOnly(app);
            Check(app.MainCalls == 0, "Real credential failures cannot invoke the workspace factory.");
        }
        await Submit(app, " User.One ", OriginalPassword);
        var firstMain = app.Windows.OfType<MainWindow>().Single();
        var oldWorkspace = (MainViewModel)firstMain.DataContext;
        Check(app.MainCalls == 1 && app.Coordinator!.Session.CurrentUser is { Username: "user.one", Provider: "Local" }
            && ((TextBlock)firstMain.FindName("CurrentUserText")).Text == "Test user",
            "Real authentication must open exactly one workspace and display the stored identity.");
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(fixture.FilePath)), "Normal UI authentication cannot rewrite accounts.");
        oldWorkspace.Logs.Add("test-only old workspace log");
        Directory.CreateDirectory(output);
        var retained = Path.Combine(output, "retained.md");
        await File.WriteAllTextAsync(retained, "test-only retained output");
        var administration = new LocalAccountAdministrationService(fixture.Store, new(), fixture.Policy);
        var generation = app.Coordinator!.Session.Generation;
        await administration.SetEnabledAsync("user.one", false);
        using (var password = Password(ResetPassword))
            await administration.ResetAsync("user.one", "Reset test user", password);
        Check(app.Coordinator.Session.IsAuthenticated && app.Coordinator.Session.Generation == generation,
            "Disable and reset take effect at next login without revoking the existing session.");
        oldWorkspace.LogoutCommand.Execute(null);
        AssertLoginOnly(app);
        Check(firstMain.IsDisposed && firstMain.DataContext is null && oldWorkspace.Logs.Count == 0
            && oldWorkspace.Documents.Count == 0 && !oldWorkspace.OpenFileCommand.CanExecute(null)
            && await File.ReadAllTextAsync(retained) == "test-only retained output",
            "Real-provider logout must dispose the old workspace and retain existing output.");
        Check(((LoginViewModel)app.Windows.OfType<LoginWindow>().Single().DataContext).Username == "",
            "Logout must require new credentials rather than reuse the prior login input.");
        await Submit(app, "user.one", ResetPassword);
        AssertLoginOnly(app);
        await administration.SetEnabledAsync("user.one", true);
        await Submit(app, "user.one", OriginalPassword);
        AssertLoginOnly(app);
        await Submit(app, "user.one", ResetPassword);
        var resetMain = app.Windows.OfType<MainWindow>().Single();
        var resetWorkspace = (MainViewModel)resetMain.DataContext;
        Check(app.MainCalls == 2 && !ReferenceEquals(resetWorkspace, oldWorkspace)
            && resetWorkspace.CurrentUserDisplayName == "Reset test user" && resetWorkspace.Documents.Count == 0,
            "Reauthentication must reread enabled state and reset password, then create a new workspace.");
        resetWorkspace.LogoutCommand.Execute(null);
        await Submit(app, "user.two", OriginalPassword);
        var secondMain = app.Windows.OfType<MainWindow>().Single();
        Check(app.MainCalls == 3 && app.Coordinator.Session.CurrentUser?.Username == "user.two"
            && !ReferenceEquals(secondMain.DataContext, resetWorkspace),
            "A different local account must get its own freshly authenticated workspace.");
        secondMain.Close();
    }

    private static async Task ConfigurationFailures(AuthenticationTestApp app, AccountFixture fixture)
    {
        var status = await Submit(app, "user.one", OriginalPassword);
        Check(!Directory.Exists(fixture.DirectoryPath), "Missing configuration cannot be initialized by login.");
        AssertConfigurationFailure(app, status);
        Directory.CreateDirectory(fixture.DirectoryPath);
        await File.WriteAllTextAsync(fixture.FilePath, "{");
        AssertConfigurationFailure(app, await Submit(app, "user.one", OriginalPassword));
        Check(await File.ReadAllTextAsync(fixture.FilePath) == "{", "Login cannot repair corrupt configuration.");
        await fixture.Write(new(2, [StoreChecks.Account()]));
        var unsupported = await File.ReadAllBytesAsync(fixture.FilePath);
        AssertConfigurationFailure(app, await Submit(app, "user.one", OriginalPassword));
        Check(Enumerable.SequenceEqual(unsupported, await File.ReadAllBytesAsync(fixture.FilePath)), "Login cannot replace an unsupported schema.");
        await fixture.Write(new(1, [StoreChecks.Account()]));
        fixture.Policy.ReadFailure = "test-only unsafe ACL";
        var valid = await File.ReadAllBytesAsync(fixture.FilePath);
        AssertConfigurationFailure(app, await Submit(app, "user.one", OriginalPassword));
        Check(Enumerable.SequenceEqual(valid, await File.ReadAllBytesAsync(fixture.FilePath)), "Rejected access cannot modify configuration.");
        fixture.Policy.ReadFailure = null;
        await Submit(app, "user.one", OriginalPassword);
        Check(app.MainCalls == 1 && app.Coordinator!.Session.IsAuthenticated,
            "Configuration repair must permit a fresh real-provider login without restarting the UI.");
        app.MainWindow.Close();
    }

    private static void AssertConfigurationFailure(AuthenticationTestApp app, string status)
    {
        AssertLoginOnly(app);
        Check(app.MainCalls == 0 && status == "本機登入尚未正確設定，請聯絡管理者",
            "Real configuration failures must stay at the gate with a safe management message.");
    }
}

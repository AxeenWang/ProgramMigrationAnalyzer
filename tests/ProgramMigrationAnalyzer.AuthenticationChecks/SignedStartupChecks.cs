using System.IO;
using System.Windows;
using System.Windows.Threading;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class SignedStartupChecks
{
    public static void Run() => CheckSupport.Run(
        ("MissingAndUnsignedStartOnlyActivation", () => {
            RunStaChild("", "signed-missing"); RunStaChild("", "signed-unsigned");
        }),
        ("StrictImportStartupModes", StrictImportStartupModes),
        ("CandidateChangedBeforeElevation", () => CandidateChangedBeforeElevation().GetAwaiter().GetResult()),
        ("CanceledElevationAndLateCompletion", () => CanceledElevationAndLateCompletion().GetAwaiter().GetResult()),
        ("EmbeddedTrustCannotBeOverridden", EmbeddedTrustCannotBeOverridden),
        ("RealSignedProviderStartupAndReauthentication", () => RunStaChild("", "signed-lifecycle", "Signed lifecycle checked.")),
        ("ImportModeNeverAuthenticates", () => {
            RunImportChild("signed-import"); RunImportChild("signed-import-denied"); RunImportChild("signed-import-cancel");
        }),
        ("ActivationReturnsOnlyToLogin", () => RunStaChild("", "signed-activate")),
        ("InstalledCandidateMustMatchPreview", () => InstalledCandidateMustMatchPreview().GetAwaiter().GetResult()));

    private static void RunImportChild(string scenario)
    {
        using var paths = new SignedAccountFixture();
        Directory.CreateDirectory(paths.DirectoryPath);
        RunStaChild(["--import-authorization", Path.Combine(paths.DirectoryPath, "candidate with spaces.key")], scenario);
    }
    private static void EmbeddedTrustCannotBeOverridden()
    {
        using var f = new SignedAccountFixture(); Directory.CreateDirectory(f.DirectoryPath);
        var external = Path.Combine(f.DirectoryPath, "authorization-public-key.pem"); File.WriteAllText(external, f.Key.PublicPem);
        var previous = Environment.GetEnvironmentVariable("PMA_AUTHORIZATION_PUBLIC_KEY");
        var working = Directory.GetCurrentDirectory();
        try
        {
            Environment.SetEnvironmentVariable("PMA_AUTHORIZATION_PUBLIC_KEY", external); Directory.SetCurrentDirectory(f.DirectoryPath);
            Check(EmbeddedAuthorizationTrust.Load().KeyId is null, "External key files, environment and working directory cannot add development trust.");
        }
        finally { Environment.SetEnvironmentVariable("PMA_AUTHORIZATION_PUBLIC_KEY", previous); Directory.SetCurrentDirectory(working); }
    }
    private static async Task InstalledCandidateMustMatchPreview()
    {
        using var f = new SignedAccountFixture(); Directory.CreateDirectory(f.DirectoryPath);
        var candidate = Path.Combine(f.DirectoryPath, "candidate.key"); await File.WriteAllBytesAsync(candidate, f.Issue());
        using var vm = new AuthorizationActivationViewModel(f.Store, f.Key.Verifier,
            new TestImportLauncher { Handler = async (_, ct) => {
                await f.Store.ImportAsync(f.Issue(2), ct: ct); return AuthorizationImportProcessResult.Succeeded;
            } });
        var activated = 0; vm.Activated += () => activated++; await vm.PreviewAsync(candidate); await vm.ImportAsync();
        Check(activated == 0 && !vm.CanImport, "A successful child with different installed payload must not activate.");
    }

    private static void StrictImportStartupModes()
    {
        Check(AuthorizationStartupRequest.Parse([]).Mode == AuthorizationStartupMode.Normal, "Normal startup rejected.");
        var path = Path.Combine(Path.GetTempPath(), "PMA candidate with spaces.key");
        Check(AuthorizationStartupRequest.Parse(["--import-authorization", path]).AbsolutePath == path, "Absolute candidate rejected.");
        foreach (var args in new[] { new[] { "--configure-local-account" }, new[] { "--skip-login" },
            new[] { "--import-authorization" }, new[] { "--import-authorization", "relative.key" },
            new[] { "--import-authorization", path, "extra" }, new[] { "--IMPORT-AUTHORIZATION", path } })
            Throws<ArgumentException>(() => AuthorizationStartupRequest.Parse(args));
    }
    private static async Task CandidateChangedBeforeElevation()
    {
        using var f = new SignedAccountFixture(); Directory.CreateDirectory(f.DirectoryPath);
        var candidate = Path.Combine(f.DirectoryPath, "candidate.key");
        await File.WriteAllBytesAsync(candidate, f.Issue());
        var launcher = new TestImportLauncher { Handler = (_, _) => throw new InvalidOperationException("Changed candidate must not elevate.") };
        using var vm = new AuthorizationActivationViewModel(f.Store, f.Key.Verifier, launcher);
        var activated = 0; vm.Activated += () => activated++;
        await vm.PreviewAsync(candidate);
        await File.WriteAllBytesAsync(candidate, f.Issue(2));
        await vm.ImportAsync();
        Check(activated == 0 && !File.Exists(f.FilePath), "A changed candidate must not install or activate.");
    }
    private static async Task CanceledElevationAndLateCompletion()
    {
        using var f = new SignedAccountFixture(); Directory.CreateDirectory(f.DirectoryPath);
        var candidate = Path.Combine(f.DirectoryPath, "candidate.key"); await File.WriteAllBytesAsync(candidate, f.Issue());
        var pending = new TaskCompletionSource<AuthorizationImportProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new TestImportLauncher { Handler = (_, _) => { entered.SetResult(); return pending.Task; } };
        using var vm = new AuthorizationActivationViewModel(f.Store, f.Key.Verifier, launcher);
        var activated = 0; vm.Activated += () => activated++;
        await vm.PreviewAsync(candidate); var work = vm.ImportAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); vm.Cancel();
        await f.Store.ImportAsync(await File.ReadAllBytesAsync(candidate)); pending.SetResult(AuthorizationImportProcessResult.Succeeded);
        await work; Check(activated == 0, "Late successful elevation after cancellation must not activate.");
        using var canceled = new AuthorizationActivationViewModel(f.Store, f.Key.Verifier,
            new TestImportLauncher { Handler = (_, _) => Task.FromResult(AuthorizationImportProcessResult.Canceled) });
        canceled.Activated += () => activated++; await canceled.PreviewAsync(candidate); await canceled.ImportAsync();
        Check(activated == 0, "UAC cancellation must not activate.");
    }

    internal static int RunChild(string scenario)
    {
        using var fixture = new SignedAccountFixture();
        if (scenario == "signed-unsigned")
        {
            Directory.CreateDirectory(fixture.DirectoryPath);
            File.WriteAllText(fixture.FilePath, "{\"schemaVersion\":1,\"users\":[]}");
        }
        var import = scenario.StartsWith("signed-import", StringComparison.Ordinal);
        if (scenario == "signed-lifecycle") fixture.Store.ImportAsync(fixture.Issue()).GetAwaiter().GetResult();
        var candidate = import ? Environment.GetCommandLineArgs().Last() : Path.Combine(fixture.DirectoryPath, "candidate.key");
        if (import || scenario == "signed-activate")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
            File.WriteAllBytes(candidate, fixture.Issue());
        }
        fixture.Policy.IsElevatedAdministrator = scenario != "signed-import-denied";
        AuthenticationTestApp? app = null;
        app = new(fixture.Authentication, session => new MainWindowFactory(() => app!.Coordinator!.RequestLogout(),
            Path.Combine(fixture.DirectoryPath, "output")).Create(session),
            fixture: fixture, launcher: new TestImportLauncher { Handler = async (path, ct) => {
                await fixture.Store.ImportAsync(await File.ReadAllBytesAsync(path, ct), ct: ct);
                return AuthorizationImportProcessResult.Succeeded;
            } });
        app.Resources = (ResourceDictionary)Application.LoadComponent(new Uri("/ProgramMigrationAnalyzer.App;component/Resources/ApplicationResources.xaml", UriKind.Relative));
        var result = 0;
        app.DispatcherUnhandledException += (_, e) => { Console.Error.WriteLine(e.Exception); e.Handled = true; result = 1; app.Coordinator?.Shutdown(); };
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            try
            {
                await AuthenticationTestApp.WaitForStartup(app);
                if (scenario == "signed-lifecycle") { await Lifecycle(app, fixture); return; }
                if (import)
                {
                    Check(app.Coordinator!.State == ApplicationSessionState.ImportingAuthorization && !app.Coordinator.Session.IsAuthenticated
                        && app.MainCalls == 0 && !app.Windows.OfType<LoginWindow>().Any(), "Import process must never authenticate.");
                    var vm = app.Windows.OfType<AuthorizationActivationWindow>().Single().ViewModel;
                    while (vm.IsBusy) await Task.Delay(10);
                    if (scenario == "signed-import-denied")
                    { Check(!vm.CanImport && !File.Exists(fixture.FilePath), "Non-elevated import cannot write."); vm.Cancel(); return; }
                    if (scenario == "signed-import-cancel") { vm.Cancel(); Check(!File.Exists(fixture.FilePath), "Canceled import wrote accounts."); return; }
                    Check(vm.CanImport && vm.Summary.Contains("版本：1"), "Import must display verified candidate before confirmation.");
                    await vm.ImportCommand.ExecuteAsync(null);
                    Check(File.Exists(fixture.FilePath) && app.MainCalls == 0 && !app.Coordinator.Session.IsAuthenticated
                        && app.Coordinator.State == ApplicationSessionState.Closing, "Confirmed import must exit without login or main.");
                    return;
                }
                Check(app.Coordinator!.State == ApplicationSessionState.Activation && !app.Coordinator.Session.IsAuthenticated
                    && app.MainCalls == 0 && !app.Windows.OfType<LoginWindow>().Any(),
                    "Missing or unsigned authorization must expose activation only, never login.");
                Check(!Directory.Exists(Path.Combine(fixture.DirectoryPath, "output")), "Activation cannot create output.");
                if (scenario == "signed-activate")
                {
                    var activation = app.Windows.OfType<AuthorizationActivationWindow>().Single(); var vm = activation.ViewModel;
                    await vm.PreviewAsync(candidate); CaptureActivation(activation); await vm.ImportCommand.ExecuteAsync(null);
                    Check(app.Coordinator.State == ApplicationSessionState.Login && app.MainCalls == 0
                        && !app.Coordinator.Session.IsAuthenticated && !app.Windows.OfType<AuthorizationActivationWindow>().Any(),
                        "Activation must return only to login and close activation.");
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e); result = 1; }
            finally { app.Coordinator!.Shutdown(); }
        }));
        var appExit = app.Run();
        if (import && result == 0) Check(appExit == (scenario == "signed-import" ? 0 : 2), "Import exit code did not reflect success/cancellation.");
        return result;
    }
    private static void CaptureActivation(Window window)
    {
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
            96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgramMigrationAnalyzer.sln"))) directory = directory.Parent;
        using var stream = File.Create(Path.Combine(directory!.FullName, ".codex-tmp", "2026-10-01_offline-authorization-phase-5", "activation-window.png"));
        encoder.Save(stream);
    }

    private static async Task Submit(AuthenticationTestApp app, string name, string value)
    {
        var login = app.Windows.OfType<LoginWindow>().Single(); var vm = (LoginViewModel)login.DataContext;
        ((System.Windows.Controls.TextBox)login.FindName("UsernameInput")).Text = name;
        var input = (System.Windows.Controls.PasswordBox)login.FindName("PasswordInput"); input.Password = value;
        ((System.Windows.Controls.Button)login.FindName("LoginButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await vm.LoginCommand.ExecutionTask!;
        Check(input.Password.Length == 0, "Signed login must clear its password input.");
    }
    private static async Task Lifecycle(AuthenticationTestApp app, SignedAccountFixture f)
    {
        var before = await File.ReadAllBytesAsync(f.FilePath);
        await Submit(app, "unknown", "Test@!#1");
        var vm = (LoginViewModel)app.Windows.OfType<LoginWindow>().Single().DataContext; var failure = vm.StatusMessage;
        await Submit(app, "internal.one", "wrong@!#1");
        Check(vm.StatusMessage == failure && app.MainCalls == 0, "Wrong and unknown accounts must fail identically.");
        await Submit(app, " Internal.One ", "Test@!#1");
        var main = app.Windows.OfType<MainWindow>().Single(); var old = (MainViewModel)main.DataContext;
        var afterLogin = await File.ReadAllBytesAsync(f.FilePath);
        Check(app.MainCalls == 1 && app.Coordinator!.Session.CurrentUser is { Provider: "SignedLocal", Username: "internal.one" }
            && before.SequenceEqual(afterLogin), "Real signed login must preserve file and show signed identity.");
        var output = Path.Combine(f.DirectoryPath, "output"); Directory.CreateDirectory(output);
        var retained = Path.Combine(output, "keep.md"); await File.WriteAllTextAsync(retained, "retained"); old.Logs.Add("old log");
        var generation = app.Coordinator!.Session.Generation;
        await f.Store.ImportAsync(f.Issue(2, enabled: false, password: "Reset@!#"));
        Check(app.Coordinator.Session.IsAuthenticated && app.Coordinator.Session.Generation == generation, "Reissue cannot revoke an existing session.");
        old.LogoutCommand.Execute(null);
        Check(old.Documents.Count == 0 && old.Logs.Count == 0 && main.IsDisposed && File.ReadAllText(retained) == "retained", "Logout must clear workspace and retain output.");
        await Submit(app, "internal.one", "Reset@!#"); Check(app.MainCalls == 1, "Disabled signed account cannot relogin.");
        await f.Store.ImportAsync(f.Issue(3, password: "Reset@!#"));
        await Submit(app, "internal.one", "Test@!#1"); Check(app.MainCalls == 1, "Reset old password must fail on fresh signature read.");
        await Submit(app, "internal.one", "Reset@!#");
        var fresh = app.Windows.OfType<MainWindow>().Single();
        Check(app.MainCalls == 2 && !ReferenceEquals(old, fresh.DataContext) && ((MainViewModel)fresh.DataContext).Logs.Count == 0, "Reauthentication must create a fresh workspace.");
        app.Coordinator.RequestLogout();
        await File.WriteAllTextAsync(f.FilePath, "{\"schemaVersion\":1,\"users\":[]}");
        await Submit(app, "internal.one", "Reset@!#"); Check(app.MainCalls == 2 && !app.Coordinator.Session.IsAuthenticated, "Unsigned replacement cannot authenticate an existing login window.");
        ((LoginViewModel)app.Windows.OfType<LoginWindow>().Single().DataContext).ImportAuthorizationCommand.Execute(null);
        Check(app.Coordinator.State == ApplicationSessionState.Activation && !app.Windows.OfType<LoginWindow>().Any(), "Login update must close stale credentials and open activation.");
        Console.WriteLine("Signed lifecycle checked.");
    }
}

internal sealed class TestImportLauncher : IAuthorizationImportLauncher
{
    internal Func<string, CancellationToken, Task<AuthorizationImportProcessResult>> Handler { get; init; } =
        (_, _) => Task.FromResult(AuthorizationImportProcessResult.Canceled);
    public Task<AuthorizationImportProcessResult> ImportAsync(string path, CancellationToken ct) => Handler(path, ct);
}

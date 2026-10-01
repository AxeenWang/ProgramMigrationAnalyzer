using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class LoginChecks
{
    internal static readonly AuthenticatedUser User = new(Guid.NewGuid(), "test.user", "Test user", "Local");
    public static void Run() => CheckSupport.Run(
        ("EmptyAndDuplicateSubmission", () => EmptyAndDuplicateSubmission().GetAwaiter().GetResult()),
        ("ThrottleAndRecovery", () => ThrottleAndRecovery().GetAwaiter().GetResult()),
        ("CancelledLateSuccessIgnored", () => CancelledLateSuccessIgnored().GetAwaiter().GetResult()),
        ("SafeFailureDiagnostics", () => SafeFailureDiagnostics().GetAwaiter().GetResult()),
        ("LoginWindowClearsPasswords", () => RunStaChild("--login-window-check")));

    private static async Task EmptyAndDuplicateSubmission()
    {
        var pending = new TaskCompletionSource<AuthenticationResult>();
        var service = new FakeAuthentication { Handler = (_, _) => pending.Task };
        using var vm = new LoginViewModel(service, new ManualTimeProvider(), new AuthenticationDiagnosticLog());
        using var password = Password("Test-only login credential");
        using var empty = Password("");
        await vm.LoginCommand.ExecuteAsync(password);
        vm.Username = "test.user";
        await vm.LoginCommand.ExecuteAsync(empty);
        Check(service.Calls == 0 && !vm.IsBusy, "Empty inputs must never authenticate.");
        var task = vm.LoginCommand.ExecuteAsync(password);
        Check(vm.IsBusy && !vm.CanEdit && !vm.LoginCommand.CanExecute(password)
            && vm.CancelLoginCommand.CanExecute(null), "Pending login must block editing and duplicate login, retaining cancel.");
        await vm.LoginAsync(password, default);
        Check(service.Calls == 1, "Direct duplicate submission must also be rejected.");
        pending.SetResult(AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials));
        await task;
        Check(!vm.IsBusy && vm.CanEdit, "Failure must restore editing.");
    }

    private static async Task ThrottleAndRecovery()
    {
        var time = new ManualTimeProvider();
        var service = new FakeAuthentication();
        using var vm = new LoginViewModel(service, time, new AuthenticationDiagnosticLog()) { Username = "test.user" };
        using var password = Password("Test-only login credential");
        for (var i = 0; i < 5; i++) await vm.LoginCommand.ExecuteAsync(password);
        Check(vm.RemainingCooldownSeconds == 30 && !vm.LoginCommand.CanExecute(password), "Fifth failure must start 30-second cooldown.");
        await vm.LoginAsync(password, default);
        time.Advance(TimeSpan.FromSeconds(29));
        Check(service.Calls == 5 && vm.RemainingCooldownSeconds == 1, "Cooldown must prevent service calls and display remaining time.");
        time.Advance(TimeSpan.FromSeconds(1));
        Check(vm.RemainingCooldownSeconds == 0 && vm.LoginCommand.CanExecute(password), "Deadline must restore submission without real waiting.");
        var successes = 0;
        vm.LoginSucceeded += _ => successes++;
        service.Handler = (_, _) => Task.FromResult(AuthenticationResult.Succeeded(User));
        await vm.LoginCommand.ExecuteAsync(password);
        Check(successes == 1, "Successful retry must raise one success.");
        vm.ReportInitializationFailure();
        service.Handler = (_, _) => Task.FromResult(AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials));
        for (var i = 0; i < 4; i++) await vm.LoginCommand.ExecuteAsync(password);
        Check(vm.RemainingCooldownSeconds == 0, "Success must reset consecutive failure count.");
        await vm.LoginCommand.ExecuteAsync(password);
        Check(vm.RemainingCooldownSeconds == 30, "A fresh series of five failures must throttle again.");
        vm.Dispose();
        time.Advance(TimeSpan.FromSeconds(30));
        Check(!vm.CanEdit && !vm.LoginCommand.CanExecute(password), "Disposed countdown cannot revive login.");
    }

    private static async Task CancelledLateSuccessIgnored()
    {
        var pending = new TaskCompletionSource<AuthenticationResult>();
        var service = new FakeAuthentication { Handler = (_, _) => pending.Task };
        using var vm = new LoginViewModel(service, new ManualTimeProvider(), new AuthenticationDiagnosticLog()) { Username = "test.user" };
        using var password = Password("Test-only login credential");
        var successes = 0;
        var cancelled = 0;
        var cleared = 0;
        vm.LoginSucceeded += _ => successes++;
        vm.Cancelled += () => cancelled++;
        vm.ClearSensitiveInputsRequested += (_, _) => cleared++;
        var task = vm.LoginCommand.ExecuteAsync(password);
        vm.Cancel();
        vm.Cancel();
        pending.SetResult(AuthenticationResult.Succeeded(User));
        await task;
        Check(successes == 0 && cancelled == 1 && cleared >= 2 && !vm.IsBusy,
            "Cancellation must be idempotent and discard a provider that ignores cancellation.");
    }

    private static async Task SafeFailureDiagnostics()
    {
        var log = new AuthenticationDiagnosticLog();
        var service = new FakeAuthentication { Handler = (_, _) => throw new Exception("secret hash salt credential") };
        using var vm = new LoginViewModel(service, new ManualTimeProvider(), log) { Username = "test.user" };
        using var password = Password("secret test-only password");
        await vm.LoginCommand.ExecuteAsync(password);
        Check(!vm.IsBusy && vm.CanEdit && !vm.StatusMessage.Contains("secret"), "Exceptions must be safe and retryable.");
        service.Handler = (_, _) => Task.FromResult(AuthenticationResult.Failed(AuthenticationFailure.ConfigurationInvalid));
        await vm.LoginCommand.ExecuteAsync(password);
        Check(vm.StatusMessage == "本機登入尚未正確設定，請聯絡管理者", "Configuration failures must fail closed with the specified message.");
        service.Handler = (_, _) => Task.FromResult(AuthenticationResult.Succeeded(User));
        await vm.LoginCommand.ExecuteAsync(password);
        var text = string.Join(" ", log.Entries.Select(entry => entry.ToString()));
        Check(!text.Contains("secret") && !text.Contains("credential") && !text.Contains("salt") && !text.Contains("hash"),
            "Diagnostics must never include provider exception messages or credentials.");
        Check(log.Entries.Any(entry => entry.Kind == AuthenticationDiagnosticKind.Succeeded)
            && log.Entries.Any(entry => entry.Failure == AuthenticationFailure.UnexpectedFailure), "Log must retain safe failure classification and success.");
        Check(!typeof(LoginViewModel).GetProperties().Any(property => property.PropertyType == typeof(SecureString)),
            "ViewModel must not expose retained passwords.");
    }

    internal static int RunWindowChild()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var pending = new TaskCompletionSource<AuthenticationResult>();
        var service = new FakeAuthentication { Handler = (_, _) => pending.Task };
        using var vm = new LoginViewModel(service, TimeProvider.System, new AuthenticationDiagnosticLog()) { Username = "test.user" };
        var window = new LoginWindow(vm);
        var result = 0;
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            try
            {
                var input = (PasswordBox)window.FindName("PasswordInput");
                var username = (TextBox)window.FindName("UsernameInput");
                var submit = (Button)window.FindName("LoginButton");
                input.Password = "Test-only password";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(vm.IsBusy && !input.IsEnabled && !username.IsEnabled && !submit.IsEnabled,
                    "Login window must disable fields and submit while awaiting authentication.");
                var task = vm.LoginCommand.ExecutionTask!;
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(service.Calls == 1 && input.Password.Length == 0, "PasswordBox must clear at submission and duplicate clicks cannot authenticate.");
                pending.SetResult(AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials));
                await task;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(input.Password.Length == 0 && input.IsEnabled, "Failure must leave no password and restore editing.");
                service.Handler = (_, _) => Task.FromResult(AuthenticationResult.Succeeded(User));
                input.Password = "Test-only success password";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await vm.LoginCommand.ExecutionTask!;
                Check(input.Password.Length == 0, "Success must clear PasswordBox.");
                input.Password = "discard on close";
                window.Close();
                Check(input.Password.Length == 0 && !vm.CanEdit, "Closing must clear and invalidate credentials.");
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
            finally { window.Close(); app.Shutdown(); }
        }));
        app.Run(window);
        return result;
    }
}

internal sealed class FakeAuthentication : IAuthenticationService
{
    public int Calls { get; private set; }
    public Func<LoginRequest, CancellationToken, Task<AuthenticationResult>> Handler { get; set; } =
        (_, _) => Task.FromResult(AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials));
    public Task<AuthenticationResult> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Handler(request, cancellationToken);
    }
}

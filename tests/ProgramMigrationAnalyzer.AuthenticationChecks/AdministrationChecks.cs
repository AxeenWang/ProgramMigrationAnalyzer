using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class AdministrationChecks
{
    public static void Run() => CheckSupport.Run(
        (nameof(NonElevatedCannotManage), () => NonElevatedCannotManage().GetAwaiter().GetResult()),
        (nameof(ManageAccountLifecycle), () => ManageAccountLifecycle().GetAwaiter().GetResult()),
        (nameof(ConfirmationAndModeIsolation), () => ConfirmationAndModeIsolation().GetAwaiter().GetResult()),
        (nameof(ExplicitCreateAndResetModes), () => ExplicitCreateAndResetModes().GetAwaiter().GetResult()),
        (nameof(CanceledAdministrationCannotCommit), () => CanceledAdministrationCannotCommit().GetAwaiter().GetResult()),
        (nameof(ConfigurationWindowIsolation), ConfigurationWindowIsolation));

    private static LocalAccountAdministrationService Service(AccountFixture fixture) => new(fixture.Store, new(), fixture.Policy);

    private static async Task NonElevatedCannotManage()
    {
        using var fixture = new AccountFixture();
        fixture.Policy.IsElevatedAdministrator = false;
        using var password = Password("Test-only admin credential");
        var service = Service(fixture);
        await StoreChecks.Expect<UnauthorizedAccessException>(() => service.CreateOrResetAsync("user.one", "Test", password));
        await StoreChecks.Expect<UnauthorizedAccessException>(() => service.SetEnabledAsync("user.one", false));
        var vm = new LocalAccountConfigurationViewModel(service);
        Check(!vm.CanEdit && !vm.SubmitCommand.CanExecute(null), "Non-elevated window must disable administration.");
        vm.CancelPending();
        Check(!Directory.Exists(fixture.DirectoryPath), "Non-elevated administration must not initialize configuration.");
    }

    private static async Task ManageAccountLifecycle()
    {
        using var fixture = new AccountFixture();
        var service = Service(fixture);
        using var oldPassword = Password("Original test-only credential");
        using var newPassword = Password("New test-only credential😀  ");
        await service.CreateOrResetAsync(" User.One ", "Test user", oldPassword);
        var original = (await fixture.Store.ReadAsync()).Users.Single();
        Check(original.UserId != Guid.Empty && original.Username == "user.one" && original.IsEnabled,
            "New accounts must have a unique GUID and be enabled.");
        Check((await fixture.Authenticate("user.one", "Original test-only credential")).IsSuccess, "Created account must authenticate.");
        await service.SetEnabledAsync("user.one", false);
        await service.CreateOrResetAsync("user.one", "Updated display", newPassword);
        var reset = (await fixture.Store.ReadAsync()).Users.Single();
        Check(reset.UserId == original.UserId && !reset.IsEnabled && reset.Salt != original.Salt
            && reset.PasswordHash != original.PasswordHash, "Reset must retain identity and disabled state, and replace salt/hash.");
        await service.SetEnabledAsync("user.one", true);
        Check(!(await fixture.Authenticate("user.one", "Original test-only credential")).IsSuccess
            && (await fixture.Authenticate("user.one", "New test-only credential😀  ")).IsSuccess, "Reset must invalidate the old password.");
        var before = await File.ReadAllBytesAsync(fixture.FilePath);
        await StoreChecks.Expect<ArgumentException>(() => service.SetEnabledAsync("unknown", true));
        using var shortPassword = Password("short");
        await StoreChecks.Expect<ArgumentException>(() => service.CreateOrResetAsync("user.one", "Test", shortPassword));
        await StoreChecks.Expect<ArgumentException>(() => service.CreateOrResetAsync("user.one", " ", newPassword));
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(fixture.FilePath)), "Rejected management must preserve all data.");
    }

    private static async Task ConfirmationAndModeIsolation()
    {
        using var fixture = new AccountFixture();
        var vm = new LocalAccountConfigurationViewModel(Service(fixture)) { Username = "user.one", DisplayName = "Test" };
        using (var first = Password("Test-only credential one"))
        using (var second = Password("Test-only credential two"))
        using (var submission = new LocalAccountSubmission(first, second))
            await vm.SubmitCommand.ExecuteAsync(submission);
        Check(!Directory.Exists(fixture.DirectoryPath) && vm.StatusMessage.Contains("一致"),
            "Mismatched passwords must not reach account management.");
        using (var password = Password("Test-only credential one"))
        using (var submission = new LocalAccountSubmission(password, password))
            await vm.SubmitCommand.ExecuteAsync(submission);
        Check((await fixture.Store.ReadAsync()).Users.Single().IsEnabled, "Matching confirmation must create an account.");
        vm.Mode = LocalAccountConfigurationMode.Disable;
        Check(!vm.NeedsPassword, "Enable and disable modes must not request passwords.");
        await vm.SubmitCommand.ExecuteAsync(null);
        Check(!(await fixture.Store.ReadAsync()).Users.Single().IsEnabled, "Disable mode must work with no credentials.");
        vm.Mode = LocalAccountConfigurationMode.Enable;
        await vm.SubmitCommand.ExecuteAsync(null);
        Check((await fixture.Store.ReadAsync()).Users.Single().IsEnabled, "Enable mode must work with no credentials.");
        Check(!typeof(LocalAccountConfigurationViewModel).GetProperties().Any(property =>
            property.PropertyType == typeof(System.Security.SecureString)), "Observable VM must not retain password properties.");
    }

    private static async Task ExplicitCreateAndResetModes()
    {
        using var fixture = new AccountFixture();
        var vm = new LocalAccountConfigurationViewModel(Service(fixture)) { Username = "user.one", DisplayName = "Test" };
        using var password = Password("Test-only credential one");
        using (var submission = new LocalAccountSubmission(password, password)) await vm.SubmitCommand.ExecuteAsync(submission);
        var before = await File.ReadAllBytesAsync(fixture.FilePath);
        using (var submission = new LocalAccountSubmission(password, password)) await vm.SubmitCommand.ExecuteAsync(submission);
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(fixture.FilePath)), "Create mode must not reset an existing account.");
        vm.Mode = LocalAccountConfigurationMode.Reset;
        vm.Username = "unknown";
        using (var submission = new LocalAccountSubmission(password, password)) await vm.SubmitCommand.ExecuteAsync(submission);
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(fixture.FilePath)), "Reset mode must not implicitly create an account.");
    }

    private static async Task CanceledAdministrationCannotCommit()
    {
        using var fixture = new AccountFixture();
        var service = Service(fixture);
        using var password = Password("Test-only admin credential");
        using var cancellation = new CancellationTokenSource();
        var task = service.CreateOrResetAsync("user.one", "Test", password, cancellation.Token);
        cancellation.Cancel();
        await StoreChecks.Expect<OperationCanceledException>(() => task);
        Check(!File.Exists(fixture.FilePath), "Canceled background hashing must never commit an account.");
    }

    private static void ConfigurationWindowIsolation()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--admin-window-check");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not launch STA window check.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(20_000)) { child.Kill(); throw new InvalidOperationException("STA window check timed out."); }
        Check(child.ExitCode == 0, $"STA window check failed: {output.GetAwaiter().GetResult()} {error.GetAwaiter().GetResult()}");
    }

    internal static int RunWindowChild()
    {
        using var fixture = new AccountFixture();
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var vm = new LocalAccountConfigurationViewModel(Service(fixture)) { Username = "user.one", DisplayName = "Test" };
        var window = new LocalAccountConfigurationWindow(vm);
        var result = 0;
        application.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            try
            {
                Check(application.Windows.Cast<Window>().All(item => item is LocalAccountConfigurationWindow),
                    "Administration must not create an analyzer window.");
                var password = (PasswordBox)window.FindName("PasswordInput");
                var confirmation = (PasswordBox)window.FindName("ConfirmationInput");
                var submit = (Button)window.FindName("SubmitButton");
                password.Password = "Test-only credential one";
                confirmation.Password = "Test-only credential two";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await vm.SubmitCommand.ExecutionTask!;
                Check(password.Password.Length == 0 && confirmation.Password.Length == 0
                    && !File.Exists(fixture.FilePath), "Window must clear both passwords and reject mismatch.");
                password.Password = confirmation.Password = "Test-only credential one";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(vm.IsBusy && !password.IsEnabled && !submit.IsEnabled, "Background submission must disable editing and duplicate submits.");
                var pendingSubmission = vm.SubmitCommand.ExecutionTask;
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ReferenceEquals(pendingSubmission, vm.SubmitCommand.ExecutionTask), "Repeated clicks must not start another request.");
                await vm.SubmitCommand.ExecutionTask!;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check((await fixture.Store.ReadAsync()).Users.Count == 1 && password.Password.Length == 0
                    && confirmation.Password.Length == 0, "Window must create the account and clear secrets.");
                password.Password = confirmation.Password = "discard these credentials";
                vm.Mode = LocalAccountConfigurationMode.Disable;
                Check(!password.IsEnabled && password.Password.Length == 0 && confirmation.Password.Length == 0,
                    "Changing mode must clear and disable password fields.");
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await vm.SubmitCommand.ExecutionTask!;
                Check(!(await fixture.Store.ReadAsync()).Users[0].IsEnabled, "UI must disable without password.");
                vm.Mode = LocalAccountConfigurationMode.Reset;
                CaptureWindow(window);
                var beforeClosing = await File.ReadAllBytesAsync(fixture.FilePath);
                password.Password = confirmation.Password = "Test-only credential reset";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Close();
                await vm.SubmitCommand.ExecutionTask!;
                Check(password.Password.Length == 0 && confirmation.Password.Length == 0
                    && application.Windows.Count == 0, "Close must clear credentials without opening a main window.");
                Check(Enumerable.SequenceEqual(beforeClosing, await File.ReadAllBytesAsync(fixture.FilePath)),
                    "Close during management must retain the exact old record, including its hash.");
            }
            catch (Exception exception) { Console.Error.WriteLine(exception.Message); result = 1; }
            finally { window.Close(); application.Shutdown(); }
        }));
        application.Run(window);
        return result;
    }

    private static void CaptureWindow(Window window)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var output = File.Create(Path.Combine(root, ".codex-tmp", "2026-10-01_login-phase-2", "admin-window.png"));
        encoder.Save(output);
    }
}

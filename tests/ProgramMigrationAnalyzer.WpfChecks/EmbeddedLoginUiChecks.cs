using System.Windows;
using System.Windows.Controls;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Authentication;

namespace ProgramMigrationAnalyzer.WpfChecks;

internal static class EmbeddedLoginUiChecks
{
    internal static async Task AuthenticateStartupAsync(LoginWindow login)
    {
        var username = (TextBox)login.FindName("UsernameBox");
        var password = (PasswordBox)login.FindName("PasswordInput");
        var submit = (Button)login.FindName("LoginButton");
        var error = (TextBlock)login.FindName("ErrorText");
        Click(submit);
        Check(submit.IsEnabled && !string.IsNullOrEmpty(error.Text) && login.IsVisible,
            "Empty input must stay at login without starting a validation request.");
        username.Text = EmbeddedLoginChecks.Username;
        password.Password = "incorrect";
        Click(submit);
        Check(password.Password.Length == 0 && !submit.IsEnabled, "Submission must clear the password and prevent duplicate requests immediately.");
        await WaitUntilAsync(() => submit.IsEnabled);
        var wrongPasswordMessage = error.Text;
        Check(!string.IsNullOrEmpty(wrongPasswordMessage) && login.IsVisible,
            "Incorrect credentials must remain at login with a safe error.");
        username.Text = "unknown";
        password.Password = EmbeddedLoginChecks.Password;
        Click(submit);
        await WaitUntilAsync(() => submit.IsEnabled);
        Check(error.Text == wrongPasswordMessage, "Unknown account and wrong password must have the same error.");

        await CheckRepeatedSubmissionAsync();
        await CheckClosedWindowIgnoresSuccessAsync(false);
        await CheckClosedWindowIgnoresSuccessAsync(true);
        await CheckVerifierFailureAsync();

        username.Text = EmbeddedLoginChecks.Username;
        password.Password = EmbeddedLoginChecks.Password;
        Click(submit);
        await WaitUntilAsync(() => !login.IsVisible);
        Check(password.Password.Length == 0, "Successful login must not retain the password in the control.");
        Console.WriteLine("PASS login UI: uniform failures, password clearing, duplicate requests, cancel/late success and exact credential acceptance.");
    }

    private static async Task CheckRepeatedSubmissionAsync()
    {
        using var verifier = new ControlledVerifier();
        var window = new LoginWindow(verifier);
        var successes = 0;
        window.AuthenticationSucceeded += (_, _) => successes++;
        window.Show();
        try
        {
            Fill(window);
            var submit = (Button)window.FindName("LoginButton");
            Click(submit);
            await verifier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Click(submit);
            verifier.Release();
            await WaitUntilAsync(() => successes > 0);
            await Task.Delay(200);
            Check(successes == 1 && verifier.Attempts == 1, "Duplicate submissions must perform one validation and emit one success.");
        }
        finally { verifier.Release(); window.Close(); }
    }

    private static async Task CheckClosedWindowIgnoresSuccessAsync(bool useCaptionClose)
    {
        using var verifier = new ControlledVerifier();
        var window = new LoginWindow(verifier);
        var successes = 0;
        window.AuthenticationSucceeded += (_, _) => successes++;
        window.Show();
        Fill(window);
        Click((Button)window.FindName("LoginButton"));
        await verifier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Click(useCaptionClose ? WindowChromeChecks.Button(window, "CloseButton") : (Button)window.FindName("CancelButton"));
        await WaitUntilAsync(() => !window.IsVisible);
        Check(!window.IsVisible && ((PasswordBox)window.FindName("PasswordInput")).Password.Length == 0,
            "Cancel must close the window and clear password input.");
        verifier.Release();
        await verifier.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        Check(successes == 0, "A success returned after cancellation must be ignored.");
    }

    private static async Task CheckVerifierFailureAsync()
    {
        var window = new LoginWindow(new ThrowingVerifier());
        var successes = 0;
        window.AuthenticationSucceeded += (_, _) => successes++;
        window.Show();
        try
        {
            Fill(window);
            Click((Button)window.FindName("LoginButton"));
            await WaitUntilAsync(() => ((Button)window.FindName("LoginButton")).IsEnabled);
            Check(successes == 0 && !((TextBlock)window.FindName("ErrorText")).Text.Contains("test-secret", StringComparison.Ordinal),
                "Unexpected verifier failure must not authenticate or expose exception details.");
        }
        finally { window.Close(); }
    }

    private static void Fill(LoginWindow window)
    {
        ((TextBox)window.FindName("UsernameBox")).Text = EmbeddedLoginChecks.Username;
        ((PasswordBox)window.FindName("PasswordInput")).Password = EmbeddedLoginChecks.Password;
    }

    internal static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Login UI did not reach the expected state.");
            await Task.Delay(15);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ThrowingVerifier : ILoginVerifier
    {
        public bool Verify(string username, string password) => throw new InvalidOperationException("test-secret must not be displayed");
    }

    private sealed class ControlledVerifier : ILoginVerifier, IDisposable
    {
        private readonly ManualResetEventSlim gate = new();
        private readonly ILoginVerifier realVerifier = EmbeddedLoginChecks.CreateVerifier();
        private int attempts;
        internal int Attempts => attempts;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Verify(string username, string password)
        {
            Interlocked.Increment(ref attempts);
            Started.TrySetResult();
            try
            {
                if (!gate.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Controlled verifier was not released.");
                return realVerifier.Verify(username, password);
            }
            finally { Finished.TrySetResult(); }
        }
        internal void Release() => gate.Set();
        public void Dispose() => gate.Dispose();
    }
}

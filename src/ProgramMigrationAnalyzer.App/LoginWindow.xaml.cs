using System.Windows;
using ProgramMigrationAnalyzer.App.Authentication;
using ProgramMigrationAnalyzer.App.Behaviors;

namespace ProgramMigrationAnalyzer.App;

public partial class LoginWindow : Window
{
    private readonly ILoginVerifier verifier;
    private readonly CancellationTokenSource lifetime = new();
    private bool busy;
    private bool closed;
    private bool authenticated;

    public event EventHandler? AuthenticationSucceeded;

    public LoginWindow(ILoginVerifier verifier)
    {
        this.verifier = verifier;
        InitializeComponent();
        Title = $"{Title} ({typeof(LoginWindow).Assembly.GetName().Version})";
        CustomWindowFrame.Attach(this);
        Loaded += (_, _) => UsernameBox.Focus();
        Closed += (_, _) =>
        {
            closed = true;
            PasswordInput.Clear();
            lifetime.Cancel();
            lifetime.Dispose();
        };
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (busy || closed || authenticated) return;
        var username = UsernameBox.Text;
        var password = PasswordInput.Password;
        PasswordInput.Clear();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ErrorText.Text = "請輸入帳號與密碼。";
            PasswordInput.Focus();
            return;
        }
        busy = true;
        SetInputsEnabled(false);
        ErrorText.Text = "正在驗證…";
        var token = lifetime.Token;
        try
        {
            var valid = await Task.Run(() => verifier.Verify(username, password), token);
            password = string.Empty;
            if (closed || token.IsCancellationRequested) return;
            if (valid)
            {
                authenticated = true;
                ErrorText.Text = string.Empty;
                AuthenticationSucceeded?.Invoke(this, EventArgs.Empty);
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            if (!closed) ErrorText.Text = "帳號或密碼不正確。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!closed) ErrorText.Text = "無法完成登入，請重新開啟程式。";
        }
        finally
        {
            password = string.Empty;
            busy = false;
            if (!closed && !authenticated)
            {
                SetInputsEnabled(true);
                PasswordInput.Focus();
            }
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        UsernameBox.IsEnabled = enabled;
        PasswordInput.IsEnabled = enabled;
        LoginButton.IsEnabled = enabled;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

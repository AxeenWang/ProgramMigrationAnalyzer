using System.Windows;
using ProgramMigrationAnalyzer.App.Authentication;
using ProgramMigrationAnalyzer.App.Services;

namespace ProgramMigrationAnalyzer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            if (e.Args.Length != 0) throw new InvalidOperationException("Startup arguments are not supported.");
            var login = new LoginWindow(CreateLoginVerifier());
            var leavingLogin = false;
            MainWindow = login;
            login.Closed += (_, _) => { if (!leavingLogin) Shutdown(); };
            login.AuthenticationSucceeded += (_, _) =>
            {
                leavingLogin = true;
                try
                {
                    var workspace = CreateMainWindow();
                    MainWindow = workspace;
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                    workspace.Show();
                    login.Close();
                }
                catch (Exception)
                {
                    ShowStartupFailure();
                    login.Close();
                }
            };
            login.Show();
        }
        catch (Exception)
        {
            ShowStartupFailure();
        }
    }

    private void ShowStartupFailure()
    {
        MainWindow = new Window
        {
            Title = "程式移植分析儀", Width = 440, SizeToContent = SizeToContent.Height,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "程式無法啟動，請重新開啟或聯絡管理者。", Margin = new Thickness(24)
            }
        };
        MainWindow.Closed += (_, _) => Shutdown(1);
        MainWindow.Show();
    }

    protected virtual ILoginVerifier CreateLoginVerifier() => EmbeddedLoginVerifier.FromEmbeddedResource();

    protected virtual MainWindow CreateMainWindow() => new MainWindowFactory().Create();
}

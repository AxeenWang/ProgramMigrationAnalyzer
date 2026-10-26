using System.Windows;
using ProgramMigrationAnalyzer.App.Services;

namespace ProgramMigrationAnalyzer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            MainWindow = CreateMainWindow();
            MainWindow.Show();
        }
        catch (Exception)
        {
            MainWindow = new Window
            {
                Title = "程式移植分析儀", Width = 440, SizeToContent = SizeToContent.Height,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "程式無法啟動，請重新開啟。", Margin = new Thickness(24)
                }
            };
            MainWindow.Closed += (_, _) => Shutdown(1);
            MainWindow.Show();
        }
    }

    protected virtual MainWindow CreateMainWindow() => new MainWindowFactory().Create();
}

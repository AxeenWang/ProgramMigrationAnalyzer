using System.Windows;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.App;

public partial class App : Application
{
    private ApplicationSessionCoordinator? _coordinator;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            _coordinator = CreateCoordinator();
            var request = AuthorizationStartupRequest.Parse(e.Args);
            await _coordinator.StartAsync(request);
        }
        catch (Exception)
        {
            if (_coordinator is not null) _coordinator.RejectStartup();
            else
            {
                var error = new Window { Title = "程式移植分析儀", Width = 440, SizeToContent = SizeToContent.Height,
                    Content = new System.Windows.Controls.TextBlock { Text = "程式無法啟動，請重新開啟或聯絡管理者。", Margin = new Thickness(24) } };
                error.Closed += (_, _) => Shutdown(1); MainWindow = error; error.Show();
            }
        }
    }
    protected virtual ApplicationSessionCoordinator CreateCoordinator()
    {
        var policy = new WindowsLocalAccountAccessPolicy();
        var verifier = new SignedAuthorizationVerifier(EmbeddedAuthorizationTrust.Load());
        var store = new SignedLocalAccountStore(SignedLocalAccountStore.DefaultFilePath, policy, verifier);
        var hasher = new LocalPasswordHasher();
        ApplicationSessionCoordinator? coordinator = null;
        var factory = new MainWindowFactory(() => coordinator!.RequestLogout());
        coordinator = new(this, new SignedLocalAuthenticationService(store, hasher), factory.Create, store,
            elevated => new AuthorizationActivationWindow(new AuthorizationActivationViewModel(store, verifier,
                new AuthorizationImportLauncher(), elevated, policy)),
            TimeProvider.System, new AuthenticationDiagnosticLog());
        return coordinator;
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose();
        base.OnExit(e);
    }
}

using System.Windows;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.App;

public partial class App : Application
{
    private ApplicationSessionCoordinator? _coordinator;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _coordinator = CreateCoordinator();
        try { _coordinator.Start(StartupModeParser.Parse(e.Args)); }
        catch (Exception) { _coordinator.RejectStartup(); }
    }
    protected virtual ApplicationSessionCoordinator CreateCoordinator()
    {
        var policy = new WindowsLocalAccountAccessPolicy();
        var store = new LocalAccountStore(LocalAccountStore.DefaultFilePath, policy);
        var hasher = new LocalPasswordHasher();
        ApplicationSessionCoordinator? coordinator = null;
        var factory = new MainWindowFactory(() => coordinator!.RequestLogout());
        coordinator = new(this, new LocalAuthenticationService(store, hasher), factory.Create,
            () => new LocalAccountConfigurationWindow(new LocalAccountConfigurationViewModel(
                new LocalAccountAdministrationService(store, hasher, policy))),
            TimeProvider.System, new AuthenticationDiagnosticLog());
        return coordinator;
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose();
        base.OnExit(e);
    }
}

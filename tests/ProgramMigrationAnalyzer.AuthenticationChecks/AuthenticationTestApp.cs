using System.Windows;
using System.Windows.Threading;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core.Authentication;
namespace ProgramMigrationAnalyzer.AuthenticationChecks;
internal sealed class AuthenticationTestApp : ProgramMigrationAnalyzer.App.App
{
    private readonly IAuthenticationService _authentication;
    private readonly Func<IUserSession, MainWindow> _mainFactory;
    private readonly TimeProvider _time;
    private readonly SignedAccountFixture _fixture;
    private readonly IAuthorizationImportLauncher _launcher;
    internal AuthenticationTestApp(IAuthenticationService authentication, Func<IUserSession, MainWindow> mainFactory,
        TimeProvider? timeProvider = null, SignedAccountFixture? fixture = null, IAuthorizationImportLauncher? launcher = null)
    {
        _authentication = authentication; _mainFactory = mainFactory; _time = timeProvider ?? TimeProvider.System;
        _launcher = launcher ?? new TestImportLauncher();
        _fixture = fixture ?? new SignedAccountFixture();
        if (fixture is null)
        {
            _fixture.Store.ImportAsync(_fixture.Issue()).GetAwaiter().GetResult();
            Exit += (_, _) => _fixture.Dispose();
        }
    }
    public ApplicationSessionCoordinator? Coordinator { get; private set; }
    public int MainCalls { get; private set; }
    public bool FailFirstMain { get; set; }
    protected override ApplicationSessionCoordinator CreateCoordinator() => Coordinator = new(this, _authentication, session => {
        MainCalls++;
        if (FailFirstMain && MainCalls == 1) throw new InvalidOperationException("test-only initialization failure");
        return _mainFactory(session);
    }, _fixture.Store, elevated => new AuthorizationActivationWindow(new AuthorizationActivationViewModel(
        _fixture.Store, _fixture.Key.Verifier, _launcher, elevated, _fixture.Policy)), _time, new AuthenticationDiagnosticLog());
    internal static async Task WaitForStartup(AuthenticationTestApp app)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (app.Coordinator?.State is null or ApplicationSessionState.Starting or ApplicationSessionState.InspectingAuthorization)
        {
            if (DateTime.UtcNow > deadline) throw new InvalidOperationException("Startup inspection timed out.");
            await Task.Delay(10);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }
}

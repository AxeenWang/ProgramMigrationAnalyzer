using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.Core;
using ProgramMigrationAnalyzer.Core.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class AccessChecks
{
    public static void Run() => CheckSupport.Run(
        ("SignedOutCommandsHaveNoEffects", () => SignedOutCommandsHaveNoEffects().GetAwaiter().GetResult()),
        ("SessionChangesDuringDialog", () => SessionChangesDuringDialog().GetAwaiter().GetResult()),
        ("SessionChangesDuringAwait", () => SessionChangesDuringAwait().GetAwaiter().GetResult()),
        ("BusyLogoutRejected", () => BusyLogoutRejected().GetAwaiter().GetResult()),
        ("DisposeCancelsAndUnsubscribes", () => DisposeCancelsAndUnsubscribes().GetAwaiter().GetResult()),
        ("LogoutClearsWorkspaceButKeepsFiles", () => RunStaChild("", "logout")));

    private static async Task SignedOutCommandsHaveNoEffects()
    {
        using var fixture = new AccessFixture(false);
        var vm = fixture.CreateViewModel();
        fixture.Seed(vm);
        Check(!vm.OpenFileCommand.CanExecute(null) && !vm.OpenFolderCommand.CanExecute(null)
            && !vm.AnalyzeCommand.CanExecute(null) && !vm.TranslateCommand.CanExecute(null)
            && !vm.SaveMarkdownCommand.CanExecute(null) && !vm.OpenOutputFolderCommand.CanExecute(null)
            && !vm.LogoutCommand.CanExecute(null), "Every protected command must be disabled when signed out.");
        await vm.OpenFileCommand.ExecuteAsync(null);
        await vm.OpenFolderCommand.ExecuteAsync(null);
        await vm.AnalyzeCommand.ExecuteAsync(null);
        await vm.TranslateCommand.ExecuteAsync(null);
        await vm.SaveMarkdownCommand.ExecuteAsync(null);
        vm.OpenOutputFolderCommand.Execute(null);
        vm.LogoutCommand.Execute(null);
        Check(fixture.Dialogs.Calls == 0 && fixture.Pipeline.TotalCalls == 0
            && !Directory.Exists(fixture.Output) && !File.Exists(fixture.SavePath), "Direct signed-out execution must have no external effects.");
        vm.Dispose();
    }

    private static async Task SessionChangesDuringDialog()
    {
        foreach (var command in new[] { "file", "folder", "save" })
        {
            using var fixture = new AccessFixture();
            using var vm = fixture.CreateViewModel();
            fixture.Seed(vm);
            fixture.Dialogs.OnDialog = fixture.Session.SignOut;
            if (command == "file") await vm.OpenFileCommand.ExecuteAsync(null);
            else if (command == "folder") await vm.OpenFolderCommand.ExecuteAsync(null);
            else await vm.SaveMarkdownCommand.ExecuteAsync(null);
            Check(fixture.Dialogs.Calls == 1 && fixture.Pipeline.TotalCalls == 0
                && !File.Exists(fixture.SavePath) && vm.Documents.Count == 0 && vm.Logs.Count == 0,
                "A changed session on dialog return must prevent reading, saving and old UI updates.");
        }
    }

    private static async Task SessionChangesDuringAwait()
    {
        foreach (var stage in new[] { "file", "folder", "parse", "analyze", "report", "write-analysis", "translate", "write-translation" })
        {
            using var fixture = new AccessFixture();
            using var vm = fixture.CreateViewModel();
            var document = fixture.Seed(vm);
            fixture.Pipeline.Block(stage);
            var operation = stage switch
            {
                "file" => vm.OpenFileCommand.ExecuteAsync(null),
                "folder" => vm.OpenFolderCommand.ExecuteAsync(null),
                "translate" or "write-translation" => vm.TranslateCommand.ExecuteAsync(null),
                _ => vm.AnalyzeCommand.ExecuteAsync(null)
            };
            await fixture.Pipeline.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Session.ReplaceUser();
            var snapshot = vm.StatusMessage;
            fixture.Pipeline.Release();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Check(vm.Documents.Count == 0 && vm.SelectedDocument is null && vm.Logs.Count == 0
                && vm.StatusMessage == snapshot && document.Analysis is null && document.Markdown == ""
                && document.TranslationCode == "" && document.SourceText == "",
                $"Late {stage} completion must not update an obsolete workspace.");
            Check(fixture.Pipeline.Count("write-analysis") == (stage == "write-analysis" ? 1 : 0)
                && fixture.Pipeline.Count("write-translation") == (stage == "write-translation" ? 1 : 0),
                $"Session change during {stage} must prevent all subsequent writes.");
            Check(fixture.Pipeline.LastToken.IsCancellationRequested, "Session change must cancel the operation token.");
        }
    }

    private static async Task BusyLogoutRejected()
    {
        using var fixture = new AccessFixture();
        using var vm = fixture.CreateViewModel();
        fixture.Pipeline.Block("file");
        var requests = 0;
        vm.LogoutRequested += () => requests++;
        var task = vm.OpenFileCommand.ExecuteAsync(null);
        await fixture.Pipeline.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(vm.IsBusy && !vm.LogoutCommand.CanExecute(null) && !vm.OpenOutputFolderCommand.CanExecute(null), "Busy work must disable logout and output actions.");
        vm.LogoutCommand.Execute(null);
        Check(requests == 0 && fixture.Session.IsAuthenticated && vm.StatusMessage.Contains("等待"), "Direct busy logout must also be refused with a wait prompt.");
        fixture.Pipeline.Release();
        await task;
        vm.LogoutCommand.Execute(null);
        Check(requests == 1, "Idle authenticated logout must request the coordinator.");
    }

    private static async Task DisposeCancelsAndUnsubscribes()
    {
        using var fixture = new AccessFixture();
        var vm = fixture.CreateViewModel();
        fixture.Seed(vm);
        fixture.Pipeline.Block("file");
        var task = vm.OpenFileCommand.ExecuteAsync(null);
        await fixture.Pipeline.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(fixture.Session.SubscriberCount == 1, "Workspace must subscribe to session changes.");
        vm.Dispose();
        vm.Dispose();
        Check(fixture.Session.SubscriberCount == 0 && fixture.Pipeline.LastToken.IsCancellationRequested,
            "Dispose must release its session subscription and cancel active work.");
        fixture.Pipeline.Release();
        await task;
        fixture.Session.ReplaceUser();
        Check(vm.Documents.Count == 0 && vm.Logs.Count == 0 && vm.CurrentUserDisplayName == ""
            && !vm.OpenFileCommand.CanExecute(null), "Disposed workspace cannot revive for a different user.");
    }

    internal static int RunWindowChild()
    {
        using var fixture = new AccessFixture();
        Directory.CreateDirectory(fixture.Output);
        var retained = Path.Combine(fixture.Output, "keep.md");
        File.WriteAllText(retained, "existing output");
        var authentication = new FakeAuthentication { Handler = (_, _) => Task.FromResult(AuthenticationResult.Succeeded(LoginChecks.User)) };
        AuthenticationTestApp? app = null;
        app = new(authentication, session => {
            var vm = fixture.CreateViewModel(session);
            vm.LogoutRequested += () => app!.Coordinator!.RequestLogout();
            return new MainWindow(vm);
        });
        app.Resources = (ResourceDictionary)Application.LoadComponent(
            new Uri("/ProgramMigrationAnalyzer.App;component/Resources/ApplicationResources.xaml", UriKind.Relative));
        var result = 0;
        app.DispatcherUnhandledException += (_, args) => { Console.Error.WriteLine(args.Exception); result = 1; args.Handled = true; app.Shutdown(); };
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => {
            try
            {
                await AuthenticationTestApp.WaitForStartup(app);
                var login = app.Windows.OfType<LoginWindow>().Single();
                var loginVm = (LoginViewModel)login.DataContext;
                loginVm.Username = "test.user";
                using var password = Password("Test-only logout password");
                await loginVm.LoginCommand.ExecuteAsync(password);
                var coordinator = app.Coordinator!;
                var oldWindow = app.Windows.OfType<MainWindow>().Single();
                var vm = (MainViewModel)oldWindow.DataContext;
                var document = fixture.Seed(vm);
                vm.Logs.Add("old workspace log");
                fixture.Pipeline.Block("file");
                var work = vm.OpenFileCommand.ExecuteAsync(null);
                await fixture.Pipeline.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                coordinator.RequestLogout();
                vm.LogoutCommand.Execute(null);
                Check(coordinator.State == ApplicationSessionState.Main && coordinator.Session.IsAuthenticated,
                    "Coordinator and command must both refuse busy logout.");
                fixture.Pipeline.Release();
                await work;
                document = vm.SelectedDocument!;
                document.ApplyAnalysis(new() { Source = fixture.Pipeline.Source, Language = SourceLanguage.CSharp },
                    "current report", "<p>current report</p>", "current.md");
                document.ApplyTranslation(new() { Source = fixture.Pipeline.Source, TargetFramework = MigrationTargetFramework.Net10,
                    GeneratedCode = "current code" }, "current.cs");
                vm.LogoutCommand.Execute(null);
                Check(coordinator.State == ApplicationSessionState.Login && !coordinator.Session.IsAuthenticated
                    && vm.Documents.Count == 0 && vm.Logs.Count == 0 && vm.SelectedDocument is null
                    && document.SourceText == "" && document.Analysis is null && document.Markdown == ""
                    && document.TranslationCode == "" && oldWindow.IsDisposed && oldWindow.DataContext is null,
                    "Logout must clear session, previews, analysis, logs, subscriptions and WebView2 workspace.");
                Check(File.ReadAllText(retained) == "existing output", "Logout must retain output files.");
                Check(app.Windows.OfType<MainWindow>().Count() == 0 && app.Windows.OfType<LoginWindow>().Count() == 1,
                    "Logout must show a fresh login without exiting.");
                var newLogin = app.Windows.OfType<LoginWindow>().Single();
                Check(!ReferenceEquals(newLogin, login), "Logout must create a fresh login window.");
                var newVm = (LoginViewModel)newLogin.DataContext;
                newVm.Username = "second.user";
                authentication.Handler = (_, _) => Task.FromResult(AuthenticationResult.Succeeded(
                    new(Guid.NewGuid(), "second.user", "Second user", "Local")));
                await newVm.LoginCommand.ExecuteAsync(password);
                var newMain = app.Windows.OfType<MainWindow>().Single();
                var workspace = (MainViewModel)newMain.DataContext;
                Check(!ReferenceEquals(workspace, vm) && workspace.Documents.Count == 0 && workspace.Logs.Count == 0
                    && workspace.CurrentUserDisplayName == "Second user", "Reauthentication must create a fresh workspace and show the new user.");
                Check(!vm.OpenFileCommand.CanExecute(null), "The old workspace must remain unusable after reauthentication.");
                workspace.LogoutCommand.Execute(null);
                ((LoginViewModel)app.Windows.OfType<LoginWindow>().Single().DataContext).Cancel();
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; app.Coordinator?.Shutdown(); }
        }));
        app.Run();
        return result;
    }
}

internal sealed class FakeUserSession : IUserSession
{
    private EventHandler? _changed;
    public FakeUserSession(bool authenticated = true) { if (authenticated) CurrentUser = LoginChecks.User; }
    public bool IsAuthenticated => CurrentUser is not null;
    public AuthenticatedUser? CurrentUser { get; private set; }
    public long Generation { get; private set; }
    public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;
    public event EventHandler? Changed { add => _changed += value; remove => _changed -= value; }
    public void SignOut() { CurrentUser = null; Generation++; _changed?.Invoke(this, EventArgs.Empty); }
    public void ReplaceUser() { CurrentUser = new(Guid.NewGuid(), "another.user", "Another user", "Local"); Generation++; _changed?.Invoke(this, EventArgs.Empty); }
}

internal sealed class AccessFixture : IDisposable
{
    private readonly string _root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    public AccessFixture(bool authenticated = true)
    {
        Session = new(authenticated);
        DirectoryPath = Path.Combine(_root, ".codex-tmp", "2026-10-01_login-phase-3", Guid.NewGuid().ToString("N"));
        Output = Path.Combine(DirectoryPath, "output");
        SavePath = Path.Combine(DirectoryPath, "save.md");
        Dialogs = new() { Path = SavePath };
        Pipeline = new(Output);
    }
    public string DirectoryPath { get; }
    public string Output { get; }
    public string SavePath { get; }
    public FakeUserSession Session { get; }
    public AccessDialogs Dialogs { get; }
    public AccessPipeline Pipeline { get; }
    public MainViewModel CreateViewModel(IUserSession? session = null) =>
        new(Pipeline, [Pipeline], Pipeline, Pipeline, Pipeline, Pipeline, Dialogs, session ?? Session);
    public SourceDocumentViewModel Seed(MainViewModel vm)
    {
        var document = new SourceDocumentViewModel(Pipeline.Source);
        document.ApplyAnalysis(new() { Source = Pipeline.Source, Language = SourceLanguage.CSharp }, "old report", "<p>old report</p>", "old.md");
        document.ApplyTranslation(new() { Source = Pipeline.Source, TargetFramework = MigrationTargetFramework.Net10, GeneratedCode = "old code" }, "old.cs");
        vm.Documents.Add(document);
        vm.SelectedDocument = document;
        return document;
    }
    public void Dispose()
    {
        Pipeline.Release();
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}

internal sealed class AccessDialogs : IFileDialogService
{
    public string Path { get; init; } = "";
    public int Calls { get; private set; }
    public Action? OnDialog { get; set; }
    private string Select() { Calls++; OnDialog?.Invoke(); return Path; }
    public string? SelectSourceFile() => Select();
    public string? SelectSourceFolder() => Select();
    public string? SelectMarkdownSavePath(string suggestedFileName) => Select();
    public void ShowError(string title, string message) => throw new InvalidOperationException($"Unexpected dialog: {title} {message}");
}

internal sealed class AccessPipeline(string output) : ISourceFileService, ISourceParser, ISourceAnalyzer, IMarkdownReportGenerator, ISourceTranslator, IOutputWriter
{
    private readonly Dictionary<string, int> _calls = [];
    private TaskCompletionSource? _gate;
    private string? _blocked;
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken LastToken { get; private set; }
    public SourceDocument Source { get; } = new() { FilePath = Path.Combine(output, "source.cs"), FileName = "source.cs", Content = "class Test {}", Language = SourceLanguage.CSharp };
    public string OutputDirectory => output;
    public int TotalCalls => _calls.Values.Sum();
    public int Count(string stage) => _calls.GetValueOrDefault(stage);
    public void Block(string stage) { _blocked = stage; _gate = new(TaskCreationOptions.RunContinuationsAsynchronously); Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public void Release() => _gate?.TrySetResult();
    private async Task Step(string stage, CancellationToken token)
    {
        _calls[stage] = Count(stage) + 1;
        LastToken = token;
        if (_blocked != stage) return;
        Entered.TrySetResult();
        await _gate!.Task;
        // Intentionally ignore cancellation so the real workspace must reject stale results itself.
    }
    public bool CanParse(SourceDocument source) => true;
    public bool CanTranslate(SourceLanguage language) => true;
    public async Task<SourceDocument> LoadFileAsync(string path, SourceLanguage languageOverride = SourceLanguage.Unknown, CancellationToken cancellationToken = default)
    { await Step("file", cancellationToken); return Source; }
    public async Task<IReadOnlyList<SourceDocument>> LoadFolderAsync(string path, SourceLanguage languageOverride = SourceLanguage.Unknown, CancellationToken cancellationToken = default)
    { await Step("folder", cancellationToken); return [Source]; }
    public async Task<AnalysisResult> ParseAsync(SourceDocument source, CancellationToken cancellationToken = default)
    { await Step("parse", cancellationToken); return new() { Source = source, Language = source.Language }; }
    public async Task<AnalysisResult> AnalyzeAsync(AnalysisResult result, CancellationToken cancellationToken = default)
    { await Step("analyze", cancellationToken); return result; }
    public string Generate(AnalysisResult result, MigrationTargetFramework targetFramework)
    { Step("report", LastToken).GetAwaiter().GetResult(); return "new report"; }
    public async Task<TranslationResult> TranslateAsync(AnalysisResult analysis, MigrationTargetFramework targetFramework, CancellationToken cancellationToken = default)
    { await Step("translate", cancellationToken); return new() { Source = analysis.Source, TargetFramework = targetFramework, GeneratedCode = "new code" }; }
    public async Task<string> WriteAnalysisAsync(SourceDocument source, string markdown, CancellationToken cancellationToken = default)
    { await Step("write-analysis", cancellationToken); return Path.Combine(output, "new.md"); }
    public async Task<string> WriteTranslationAsync(TranslationResult result, CancellationToken cancellationToken = default)
    { await Step("write-translation", cancellationToken); return Path.Combine(output, "new.cs"); }
}

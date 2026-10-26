using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ProgramMigrationAnalyzer.App;
using ProgramMigrationAnalyzer.App.Authentication;

namespace ProgramMigrationAnalyzer.WpfChecks;

internal static class LoginStartupChecks
{
    private const string ScenarioVariable = "PMA_LOGIN_STARTUP_CHECK";

    internal static int? RunChildIfRequested()
    {
        var scenario = Environment.GetEnvironmentVariable(ScenarioVariable);
        if (scenario is null) return null;
        var app = new StartupProbeApp(scenario);
        app.Resources = (ResourceDictionary)Application.LoadComponent(
            new Uri("/ProgramMigrationAnalyzer.App;component/Resources/ApplicationResources.xaml", UriKind.Relative));
        Exception? failure = null;
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            Console.WriteLine("Startup probe dispatcher: " + scenario);
            try
            {
                if (app.MainCalls != 0 || app.Windows.OfType<MainWindow>().Any())
                    throw new InvalidOperationException("Startup gate created a workspace without authentication.");
                if (scenario == "cancel")
                {
                    var login = app.Windows.OfType<LoginWindow>().Single();
                    EmbeddedLoginUiChecks.Click(WindowChromeChecks.Button(login, "CloseButton"));
                    Console.WriteLine("Startup probe cancel closed window.");
                }
                else
                {
                    if (app.Windows.OfType<LoginWindow>().Any()) throw new InvalidOperationException("Invalid startup must not reach login.");
                    app.MainWindow.Close();
                }
            }
            catch (Exception exception) { failure = exception; app.Shutdown(2); }
        }));
        Console.WriteLine("Startup probe run: " + scenario);
        var result = app.Run();
        if (failure is not null) { Console.Error.WriteLine(failure); return 2; }
        var expectedExit = scenario == "cancel" ? 0 : 1;
        if (result != expectedExit || app.MainCalls != 0) return 3;
        Console.WriteLine("PASS isolated startup: " + scenario);
        return 0;
    }

    internal static void RunParent()
    {
        foreach (var scenario in new[] { "cancel", "malformed", "unsupported-argument" })
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ProgramMigrationAnalyzer.WpfChecks.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.Environment[ScenarioVariable] = scenario;
            if (scenario == "unsupported-argument") start.ArgumentList.Add("--skip-login");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("Isolated startup check timed out: " + scenario + " " + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            }
            var details = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException("Isolated startup failed: " + scenario + " " + details);
            Console.Write(details);
        }
    }

    private sealed class StartupProbeApp(string scenario) : ProgramMigrationAnalyzer.App.App
    {
        internal int MainCalls { get; private set; }
        protected override ILoginVerifier CreateLoginVerifier() => scenario == "malformed"
            ? EmbeddedLoginChecks.CreateVerifier("{}") : base.CreateLoginVerifier();
        protected override MainWindow CreateMainWindow() { MainCalls++; return base.CreateMainWindow(); }
    }
}

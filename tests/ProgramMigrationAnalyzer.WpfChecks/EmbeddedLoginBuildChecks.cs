using System.Diagnostics;
using System.IO;

namespace ProgramMigrationAnalyzer.WpfChecks;

internal static class EmbeddedLoginBuildChecks
{
    internal static void Run(string root, string scratch)
    {
        var script = Path.Combine(root, "build", "Set-EmbeddedLogin.ps1");
        var first = Path.Combine(scratch, "credential-one.json");
        var second = Path.Combine(scratch, "credential-two.json");
        foreach (var output in new[] { first, second })
        {
            var result = Invoke($"$p = ConvertTo-SecureString '{EmbeddedLoginChecks.Password}' -AsPlainText -Force; " +
                $"$c = ConvertTo-SecureString '{EmbeddedLoginChecks.Password}' -AsPlainText -Force; " +
                $"& '{script}' -Username '{EmbeddedLoginChecks.Username}' -Password $p -Confirmation $c -OutputPath '{output}'");
            Check(result.Exit == 0 && File.Exists(output), "Credential configuration tool failed. " + result.Output);
            var json = File.ReadAllText(output);
            Check(!json.Contains(EmbeddedLoginChecks.Password, StringComparison.Ordinal)
                && EmbeddedLoginChecks.CreateVerifier(json).Verify(EmbeddedLoginChecks.Username, EmbeddedLoginChecks.Password),
                "Configuration generation must preserve the password without persisting plaintext.");
            Check(!result.Output.Contains(EmbeddedLoginChecks.Password, StringComparison.Ordinal), "Configuration output exposed the password.");
        }
        Check(File.ReadAllText(first) != File.ReadAllText(second), "Independent configurations must use fresh random salt.");
        var before = File.ReadAllBytes(first);
        var rejected = Invoke($"$p = ConvertTo-SecureString 'one' -AsPlainText -Force; " +
            $"$c = ConvertTo-SecureString 'two' -AsPlainText -Force; & '{script}' -Username test -Password $p -Confirmation $c -OutputPath '{first}' -Force");
        Check(rejected.Exit != 0 && before.SequenceEqual(File.ReadAllBytes(first)), "Mismatched passwords must not replace an existing verifier.");

        var missing = Path.Combine(scratch, "missing-credential.json");
        var build = new ProcessStartInfo("dotnet") { WorkingDirectory = root };
        foreach (var argument in new[] { "build", "src/ProgramMigrationAnalyzer.App/ProgramMigrationAnalyzer.App.csproj", "--no-restore", "-m:1", "-nr:false",
                     "-p:UseSharedCompilation=false", "-p:PmaCredentialFile=" + missing, "-v:minimal" }) build.ArgumentList.Add(argument);
        var failedBuild = Run(build);
        Check(failedBuild.Exit != 0 && failedBuild.Output.Contains("Embedded login configuration is missing", StringComparison.Ordinal),
            "A build without explicit credential configuration must fail closed. " + failedBuild.Output);
        Console.WriteLine("PASS credential build boundary: Windows PowerShell generation, fresh salts, no plaintext, mismatch preserves existing data and missing-resource build rejection.");
    }

    private static (int Exit, string Output) Invoke(string command)
    {
        var start = new ProcessStartInfo("powershell.exe");
        // A .NET child otherwise inherits PowerShell 7 modules that Windows PowerShell 5.1 cannot load.
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                     "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); $ErrorActionPreference = 'Stop'; " + command })
            start.ArgumentList.Add(argument);
        return Run(start);
    }

    private static (int Exit, string Output) Run(ProcessStartInfo start)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Credential build check timed out.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

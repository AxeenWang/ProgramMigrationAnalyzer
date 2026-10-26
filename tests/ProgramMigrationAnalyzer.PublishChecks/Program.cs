using System.Diagnostics;
using System.Text;

if (Environment.GetEnvironmentVariable("PMA_PUBLISH_CHECK_STUB") == "1")
{
    Console.OutputEncoding = new UTF8Encoding(false);
    Console.WriteLine("發布測試：中文輸出正常");
    if (args.Length == 0 || args[0] != "publish" || args.Any(a => a.Contains("PublicKey", StringComparison.OrdinalIgnoreCase)))
        return 19;
    var output = args[Array.IndexOf(args, "--output") + 1];
    Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(output, "ProgramMigrationAnalyzer.App.exe"), "new analyzer");
    if (Environment.GetEnvironmentVariable("PMA_PUBLISH_STUB_EXTRA") == "1")
        File.WriteAllText(Path.Combine(output, "unexpected.txt"), "extra");
    return Environment.GetEnvironmentVariable("PMA_PUBLISH_STUB_FAIL") == "1" ? 17 : 0;
}

using var fixture = new PublishFixture();
var previous = "previous executable";
Directory.CreateDirectory(Path.GetDirectoryName(fixture.Executable)!);
File.WriteAllText(fixture.Executable, previous);
var profile = Path.Combine(Path.GetDirectoryName(fixture.Executable)!, "ProgramMigrationAnalyzer.App.exe.WebView2", "retained.dat");
Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
File.WriteAllText(profile, "existing profile");

var failed = fixture.Run(fail: true);
Check(failed.Exit != 0 && File.ReadAllText(fixture.Executable) == previous,
    "A failed build must preserve the previous executable. " + failed.Output);
var extra = fixture.Run(extra: true);
Check(extra.Exit != 0 && File.ReadAllText(fixture.Executable) == previous,
    "Unexpected delivery files must prevent replacing the executable. " + extra.Output);
var success = fixture.Run();
Check(success.Exit == 0 && File.ReadAllText(fixture.Executable) == "new analyzer",
    "Publication must succeed without any authorization configuration. " + success.Output);
Check(success.Output.Contains("發布測試：中文輸出正常", StringComparison.Ordinal),
    "Publishing must preserve UTF-8 Chinese build output. " + success.Output);
Check(File.ReadAllText(profile) == "existing profile", "Publication removed adjacent WebView2 data.");
Check(!Directory.EnumerateDirectories(Path.Combine(fixture.DirectoryPath, "publish"), ".stage-*").Any(),
    "Publication left staging files behind.");
Console.WriteLine("Publish checks passed: no-Key publication, atomic replacement, failed build, unexpected output, retained WebView2 data and staging cleanup.");
return 0;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class PublishFixture : IDisposable
{
    public string DirectoryPath { get; }
    public string Executable => Path.Combine(DirectoryPath, "publish", "win-x64-single-file", "ProgramMigrationAnalyzer.App.exe");

    public PublishFixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ProgramMigrationAnalyzer.sln"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Publish checks must run inside the project.");
        DirectoryPath = Path.Combine(root.FullName, ".camp_codex", "2026-10-26_remove-authentication", "publish-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        File.Copy(Path.Combine(root.FullName, "publish.bat"), Path.Combine(DirectoryPath, "publish.bat"));
        File.WriteAllText(Path.Combine(DirectoryPath, "ProgramMigrationAnalyzer.sln"), "isolated script fixture");
        var source = Path.Combine(DirectoryPath, "src", "ProgramMigrationAnalyzer.App");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "App.xaml"), "isolated script fixture");
        var build = Path.Combine(DirectoryPath, "build");
        Directory.CreateDirectory(build);
        foreach (var script in new[] { "Publish-Client.ps1", "Publish-Artifact.ps1" })
            File.Copy(Path.Combine(root.FullName, "build", script), Path.Combine(build, script));
        var stub = Path.Combine(DirectoryPath, "stub");
        Directory.CreateDirectory(stub);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory).Where(p => p.EndsWith(".dll") || p.EndsWith(".json")))
            File.Copy(file, Path.Combine(stub, Path.GetFileName(file)));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ProgramMigrationAnalyzer.PublishChecks.exe"), Path.Combine(stub, "dotnet.exe"));
    }

    public (int Exit, string Output) Run(bool fail = false, bool extra = false)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = DirectoryPath,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
            Arguments = "/d /c publish.bat"
        };
        start.Environment["PATH"] = Path.Combine(DirectoryPath, "stub") + ";" + start.Environment["PATH"];
        start.Environment["PMA_PUBLISH_CHECK_STUB"] = "1";
        start.Environment["PMA_PUBLISH_STUB_FAIL"] = fail ? "1" : "0";
        start.Environment["PMA_PUBLISH_STUB_EXTRA"] = extra ? "1" : "0";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(45_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Publish fixture timed out.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}

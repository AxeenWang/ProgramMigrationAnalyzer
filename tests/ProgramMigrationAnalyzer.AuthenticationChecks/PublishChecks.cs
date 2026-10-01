using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using ProgramMigrationAnalyzer.PublishPreparation;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;
namespace ProgramMigrationAnalyzer.AuthenticationChecks;
internal static class PublishChecks
{
    internal static void Run() => CheckSupport.Run(
        ("MissingInvalidAndPrivatePemPublishFails", MissingInvalidAndPrivatePemPublishFails),
        ("RootPublishAndPublicKeySnapshot", RootPublishAndPublicKeySnapshot),
        ("FailedPublishKeepsPriorExe", FailedPublishKeepsPriorExe),
        ("IssuerAndClientDeliveryAreSeparate", IssuerAndClientDeliveryAreSeparate),
        ("NoBuildClientPublishRejected", NoBuildClientPublishRejected),
        ("PublicSnapshotRejectsOversizeAndConflictingFiles", PublicSnapshotRejectsOversizeAndConflictingFiles));
    private static void NoBuildClientPublishRejected()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ProgramMigrationAnalyzer.sln"))) root = root.Parent;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = root!.FullName, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "publish", "src/ProgramMigrationAnalyzer.App/ProgramMigrationAnalyzer.App.csproj",
            "--no-build", "--no-restore", "--configuration", "Debug", "--output",
            ".codex-tmp/2026-10-01_offline-authorization-phase-5/rejected-nobuild", "-m:1", "-nr:false" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        Check(process.WaitForExit(30_000), "No-build publication timed out.");
        Check(process.ExitCode != 0 && (output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult()).Contains("PMA_AUTH_NO_BUILD"),
            "Direct SDK no-build publish must fail at the authorization guard.");
    }
    private static void PublicSnapshotRejectsOversizeAndConflictingFiles()
    {
        using var f = new PublishFixture(); var staging = Path.Combine(f.DirectoryPath, "snapshot");
        File.WriteAllText(f.PublicPath, new string(' ', 8193)); Throws<IOException>(() => PublicKeySnapshot.Prepare(f.PublicPath, staging));
        File.WriteAllText(f.PublicPath, f.Key.PublicPem); var snapshot = PublicKeySnapshot.Prepare(f.PublicPath, staging);
        File.WriteAllText(snapshot, "conflicting snapshot"); Throws<IOException>(() => PublicKeySnapshot.Prepare(f.PublicPath, staging));
        Check(File.ReadAllText(snapshot) == "conflicting snapshot", "Conflicting snapshot must not be silently overwritten.");
    }
    private static void MissingInvalidAndPrivatePemPublishFails()
    {
        using var f = new PublishFixture();
        Check(f.Run("publish.bat").Exit != 0, "Publishing without a public key must fail.");
        File.WriteAllText(f.PublicPath, "-----BEGIN PRIVATE KEY-----\nAA==\n-----END PRIVATE KEY-----");
        Check(f.Run("publish.bat", f.PublicPath).Exit != 0, "Private PEM input must not publish.");
        File.WriteAllText(f.PublicPath, "broken public PEM");
        Check(f.Run("publish.bat", f.PublicPath).Exit != 0, "Invalid public PEM input must not publish.");
    }
    private static void RootPublishAndPublicKeySnapshot()
    {
        using var f = new PublishFixture(); File.WriteAllText(f.PublicPath, f.Key.PublicPem);
        var snapshot = PublicKeySnapshot.Prepare(f.PublicPath, Path.Combine(f.DirectoryPath, "snapshot"));
        File.WriteAllText(f.PublicPath, "source changed after snapshot");
        Check(AuthorizationTrust.FromPublicKeyPem(File.ReadAllText(snapshot)).KeyId == f.Key.KeyId, "Snapshot did not retain verified public bytes.");
        File.WriteAllText(f.PublicPath, f.Key.PublicPem);
        Directory.CreateDirectory(Path.GetDirectoryName(f.ClientExe)!);
        File.WriteAllText(f.ClientExe, "prior executable");
        var profile = Path.Combine(Path.GetDirectoryName(f.ClientExe)!, "ProgramMigrationAnalyzer.App.exe.WebView2", "retained.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!); File.WriteAllText(profile, "existing profile");
        var result = f.Run("publish.bat", f.PublicPath, mutatePublic: true);
        Check(result.Exit == 0 && File.ReadAllText(f.ClientExe) == "client:" + f.Key.KeyId,
            "Root publication must use the selected snapshot despite source changes. " + result.Output);
        Check(File.ReadAllText(profile) == "existing profile", "Publication must retain adjacent WebView2 data.");
    }
    private static void FailedPublishKeepsPriorExe()
    {
        using var f = new PublishFixture(); Directory.CreateDirectory(Path.GetDirectoryName(f.ClientExe)!);
        File.WriteAllText(f.ClientExe, "prior executable"); File.WriteAllText(f.PublicPath, f.Key.PublicPem);
        var result = f.Run("publish.bat", f.PublicPath, failPublish: true);
        Check(result.Exit != 0 && result.Output.Contains("Publication failed") && File.ReadAllText(f.ClientExe) == "prior executable",
            "A failed publish must preserve the previous executable.");
    }
    private static void IssuerAndClientDeliveryAreSeparate()
    {
        using var f = new PublishFixture(); File.WriteAllText(f.PublicPath, f.Key.PublicPem);
        var client = f.Run("publish.bat", f.PublicPath); var issuer = f.Run("publish-issuer.bat");
        Check(client.Exit == 0 && issuer.Exit == 0, "Both publishers must succeed independently. " + client.Output + issuer.Output);
        Check(File.ReadAllText(f.ClientExe) == "client:" + f.Key.KeyId && File.ReadAllText(f.IssuerExe) == "issuer",
            "Client and issuer must use separate destinations.");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(f.ClientExe)!, "*", SearchOption.AllDirectories)
            .Any(p => Path.GetFileName(p).Contains("Issuer") || p.EndsWith(".pem") || p.EndsWith(".key") || Path.GetFileName(p) == "users.json"),
            "Client delivery must not contain issuer, keys or users.");
    }
    // This fake replaces the build process only in isolated script fixtures. Real SDK publication is verified separately.
    internal static int RunDotNetStub(string[] args)
    {
        if (args.Contains("--public-key"))
        {
            var input = args[Array.IndexOf(args, "--public-key") + 1];
            var staging = args[Array.IndexOf(args, "--staging") + 1];
            try
            {
                var snapshot = PublicKeySnapshot.Prepare(input, staging);
                if (Environment.GetEnvironmentVariable("PMA_PUBLISH_STUB_MUTATE") == "1") File.WriteAllText(input, "changed after preparation");
                Console.WriteLine(snapshot); return 0;
            }
            catch { return 1; }
        }
        if (!args.Contains("publish")) return 0;
        var output = args[Array.IndexOf(args, "--output") + 1]; Directory.CreateDirectory(output);
        var issuer = args.Any(a => a.Contains("LicenseIssuer.csproj", StringComparison.Ordinal));
        var keyArgument = args.FirstOrDefault(a => a.StartsWith("-p:AuthorizationPublicKeyPath=", StringComparison.Ordinal));
        var keyId = keyArgument is null ? "legacy" : AuthorizationTrust.FromPublicKeyPem(File.ReadAllText(keyArgument.Split('=', 2)[1])).KeyId;
        File.WriteAllText(Path.Combine(output, issuer ? "ProgramMigrationAnalyzer.LicenseIssuer.exe" : "ProgramMigrationAnalyzer.App.exe"), issuer ? "issuer" : "client:" + keyId);
        return Environment.GetEnvironmentVariable("PMA_PUBLISH_STUB_FAIL") == "1" ? 17 : 0;
    }
}
internal sealed class PublishFixture : IDisposable
{
    internal readonly AuthorizationTestFixture Key = new();
    internal string DirectoryPath { get; }
    internal string PublicPath => Path.Combine(DirectoryPath, "public key with spaces.pem");
    internal string ClientExe => Path.Combine(DirectoryPath, "publish", "win-x64-single-file", "ProgramMigrationAnalyzer.App.exe");
    internal string IssuerExe => Path.Combine(DirectoryPath, "publish", "company-license-issuer", "ProgramMigrationAnalyzer.LicenseIssuer.exe");
    internal PublishFixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ProgramMigrationAnalyzer.sln"))) root = root.Parent;
        DirectoryPath = Path.Combine(root!.FullName, ".codex-tmp", "2026-10-01_offline-authorization-phase-5", "publish-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        foreach (var file in new[] { "publish.bat", "publish-issuer.bat" })
            if (File.Exists(Path.Combine(root.FullName, file))) File.Copy(Path.Combine(root.FullName, file), Path.Combine(DirectoryPath, file));
        var build = Path.Combine(DirectoryPath, "build"); Directory.CreateDirectory(build);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root.FullName, "build"), "*.ps1")) File.Copy(file, Path.Combine(build, Path.GetFileName(file)));
        var src = Path.Combine(DirectoryPath, "src", "ProgramMigrationAnalyzer.App"); Directory.CreateDirectory(src); File.WriteAllText(Path.Combine(src, "LoginWindow.xaml"), "test fixture");
        var tools = Path.Combine(DirectoryPath, "stub"); Directory.CreateDirectory(tools);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory).Where(p => p.EndsWith(".dll") || p.EndsWith(".json")))
            File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ProgramMigrationAnalyzer.AuthenticationChecks.exe"), Path.Combine(tools, "dotnet.exe"));
    }
    internal (int Exit, string Output) Run(string script, string? publicPath = null, bool failPublish = false, bool mutatePublic = false)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = DirectoryPath, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Arguments = "/d /c \"\"" + Path.Combine(DirectoryPath, script) + "\"" + (publicPath is null ? "" : " \"" + publicPath + "\"") + "\"";
        start.Environment["PATH"] = Path.Combine(DirectoryPath, "stub") + ";" + start.Environment["PATH"];
        start.Environment["PMA_PUBLISH_CHECK_STUB"] = "1";
        start.Environment["PMA_PUBLISH_STUB_FAIL"] = failPublish ? "1" : "0";
        start.Environment["PMA_PUBLISH_STUB_MUTATE"] = mutatePublic ? "1" : "0";
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(45_000)) { process.Kill(true); throw new InvalidOperationException("Publish script fixture timed out."); }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
    public void Dispose() { Key.Dispose(); if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
}

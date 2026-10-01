using System.ComponentModel;
using System.Diagnostics;
using System.IO;
namespace ProgramMigrationAnalyzer.App.Services;

internal enum AuthorizationImportProcessResult { Succeeded, Canceled, Failed }
internal interface IAuthorizationImportLauncher
{
    Task<AuthorizationImportProcessResult> ImportAsync(string absoluteCandidatePath, CancellationToken ct);
}

internal sealed class AuthorizationImportLauncher : IAuthorizationImportLauncher
{
    public async Task<AuthorizationImportProcessResult> ImportAsync(string absoluteCandidatePath, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(absoluteCandidatePath)) return AuthorizationImportProcessResult.Failed;
        ct.ThrowIfCancellationRequested();
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException())
            { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add("--import-authorization"); start.ArgumentList.Add(absoluteCandidatePath);
            using var process = Process.Start(start);
            if (process is null) return AuthorizationImportProcessResult.Failed;
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0 ? AuthorizationImportProcessResult.Succeeded
                : process.ExitCode == 2 ? AuthorizationImportProcessResult.Canceled : AuthorizationImportProcessResult.Failed;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { return AuthorizationImportProcessResult.Canceled; }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        { return AuthorizationImportProcessResult.Failed; }
    }
}

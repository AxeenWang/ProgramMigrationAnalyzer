using System.IO;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
namespace ProgramMigrationAnalyzer.AuthenticationChecks;
internal sealed class FakeAccountPolicy : ILocalAccountAccessPolicy
{
    public bool IsElevatedAdministrator { get; set; } = true;
    public string? ReadFailure { get; set; }
    public bool FailSecuringTemporary { get; set; }
    public bool RejectLockPath { get; set; }
    public bool RejectFinalValidation { get; set; }
    public bool ProbeRollbackLock { get; set; }
    public bool RollbackWasLocked { get; private set; }
    public bool SecuredTemporary { get; private set; }
    public HashSet<string> ValidatedPaths { get; } = [];
    public void ValidateReadAccess(string filePath)
    {
        ValidatedPaths.Add(filePath);
        if (ProbeRollbackLock && Path.GetFileName(filePath).StartsWith(".users-backup-", StringComparison.Ordinal))
        {
            try
            {
                using var competing = new FileStream(Path.Combine(Path.GetDirectoryName(filePath)!, "users.json.lock"),
                    FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33) { RollbackWasLocked = true; }
        }
        if (ReadFailure is not null || (RejectLockPath && filePath.EndsWith(".lock", StringComparison.Ordinal))
            || (RejectFinalValidation && SecuredTemporary && filePath.EndsWith("users.json", StringComparison.Ordinal)))
            throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.UnsafeAccess);
    }
    public void PrepareWriteAccess(string directoryPath) => Directory.CreateDirectory(directoryPath);
    public void SecureFile(string filePath)
    {
        if (Path.GetExtension(filePath) == ".tmp")
        {
            if (FailSecuringTemporary) throw new IOException("Simulated write interruption.");
            SecuredTemporary = true;
        }
    }
    public void ResetTemporaryState() => SecuredTemporary = false;
}

using System.Diagnostics;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public sealed class SignedLocalAccountStore
{
    private readonly string _filePath;
    private readonly ILocalAccountAccessPolicy _policy;
    private readonly SignedAuthorizationVerifier _verifier;

    public SignedLocalAccountStore(string filePath, ILocalAccountAccessPolicy policy, SignedAuthorizationVerifier verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    }
    public static string DefaultFilePath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.CommonApplicationData), "ProgramMigrationAnalyzer", "auth", "users.json");
    public Task<VerifiedAuthorization> ReadAsync(CancellationToken ct = default) => ReadFileAsync(_filePath, ct);

    private async Task<VerifiedAuthorization> ReadFileAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            _policy.ValidateReadAccess(path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await _verifier.ReadAndVerifyAsync(stream, ct);
        }
        catch (FileNotFoundException) { throw new AuthorizationException(AuthorizationFailure.Missing); }
        catch (DirectoryNotFoundException) { throw new AuthorizationException(AuthorizationFailure.Missing); }
        catch (LocalAccountConfigurationException error)
        {
            throw new AuthorizationException(error.Failure switch
            {
                LocalAccountConfigurationFailure.Missing => AuthorizationFailure.Missing,
                LocalAccountConfigurationFailure.InvalidData => AuthorizationFailure.InvalidData,
                LocalAccountConfigurationFailure.Busy => AuthorizationFailure.Busy,
                LocalAccountConfigurationFailure.StorageUnavailable => AuthorizationFailure.StorageUnavailable,
                _ => AuthorizationFailure.UnsafeAccess
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new AuthorizationException(AuthorizationFailure.StorageUnavailable); }
    }

    public async Task<AuthorizationImportResult> ImportAsync(ReadOnlyMemory<byte> envelope,
        Guid? confirmedReplacementAuthorizationId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_policy.IsElevatedAdministrator) throw new AuthorizationException(AuthorizationFailure.UnsafeAccess);
        var candidate = _verifier.Verify(envelope);
        var directory = Path.GetDirectoryName(_filePath)!;
        FileStream? heldLock = null;
        string? temporary = null;
        string? backup = null;
        var createdNewFile = false;
        var completed = false;
        try
        {
            _policy.PrepareWriteAccess(directory);
            var lockPath = _filePath + ".lock";
            // Still validate absent paths to reject dangling reparse points before opening.
            try { _policy.ValidateReadAccess(lockPath); }
            catch (LocalAccountConfigurationException error) when (error.Failure == LocalAccountConfigurationFailure.Missing) { }
            heldLock = await AcquireLockAsync(lockPath, ct);
            _policy.SecureFile(lockPath);
            _policy.ValidateReadAccess(lockPath);
            VerifiedAuthorization? current = null;
            try { current = await ReadAsync(ct); }
            catch (AuthorizationException error) when (error.Failure is AuthorizationFailure.Missing
                or AuthorizationFailure.InvalidData or AuthorizationFailure.InvalidSignature or AuthorizationFailure.UntrustedKey)
            { /* A valid signed import can replace an unsigned or damaged account file. */ }
            if (current is not null)
            {
                if (candidate.Payload.AuthorizationId != current.Payload.AuthorizationId)
                {
                    // Confirmation must refer to the target read under the writer lock.
                    if (confirmedReplacementAuthorizationId != current.Payload.AuthorizationId)
                        throw new AuthorizationException(AuthorizationFailure.ReplacementConfirmationRequired);
                }
                else
                {
                    if (candidate.Payload.Revision < current.Payload.Revision
                        || candidate.Payload.Revision == current.Payload.Revision
                        && candidate.PayloadFingerprint != current.PayloadFingerprint)
                        throw new AuthorizationException(AuthorizationFailure.StaleRevision);
                    if (candidate.Payload.Revision == current.Payload.Revision) return Result(candidate, false);
                }
            }
            temporary = Path.Combine(directory, $".users-{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(candidate.CopyEnvelopeBytes(), ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            _policy.SecureFile(temporary);
            var prepared = await ReadFileAsync(temporary, ct);
            if (prepared.PayloadFingerprint != candidate.PayloadFingerprint)
                throw new AuthorizationException(AuthorizationFailure.InvalidData);
            ct.ThrowIfCancellationRequested();
            if (File.Exists(_filePath))
            {
                backup = Path.Combine(directory, $".users-backup-{Guid.NewGuid():N}.tmp");
                File.Replace(temporary, _filePath, backup);
            }
            else
            {
                File.Move(temporary, _filePath);
                createdNewFile = true;
            }
            temporary = null;
            var installed = await ReadAsync(ct);
            if (installed.PayloadFingerprint != candidate.PayloadFingerprint)
                throw new AuthorizationException(AuthorizationFailure.InvalidData);
            completed = true;
            return Result(installed, true);
        }
        catch (Exception error)
        {
            // Recovery remains under the same lock, including final validation failures.
            try
            {
                if (backup is not null && File.Exists(backup))
                {
                    _policy.ValidateReadAccess(backup);
                    if (File.Exists(_filePath)) File.Replace(backup, _filePath, null);
                    else File.Move(backup, _filePath);
                    backup = null;
                }
                else if (createdNewFile) File.Delete(_filePath);
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException
                or LocalAccountConfigurationException)
            { throw new AuthorizationException(AuthorizationFailure.StorageUnavailable); }
            if (error is LocalAccountConfigurationException)
                throw new AuthorizationException(AuthorizationFailure.UnsafeAccess);
            if (error is IOException or UnauthorizedAccessException)
                throw new AuthorizationException(AuthorizationFailure.StorageUnavailable);
            throw;
        }
        finally
        {
            DeleteScratch(temporary);
            // Preserve a protected backup if recovery itself failed.
            if (completed) DeleteScratch(backup);
            if (heldLock is not null) await heldLock.DisposeAsync();
        }
    }
    private static AuthorizationImportResult Result(VerifiedAuthorization value, bool changed) =>
        new(value.Payload.AuthorizationId, value.Payload.Revision, value.PayloadFingerprint, changed);
    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33)
            {
                if (timer.Elapsed >= TimeSpan.FromSeconds(5)) throw new AuthorizationException(AuthorizationFailure.Busy);
                await Task.Delay(50, ct);
            }
        }
    }
    private static void DeleteScratch(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

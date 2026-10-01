using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace ProgramMigrationAnalyzer.LicenseIssuer.Services;

public sealed class ProtectedIssuerFileWriter
{
    public async Task WriteNewPrivateKeyAsync(string path, ReadOnlyMemory<byte> encryptedPkcs8, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        RejectRepositoryDestination(fullPath);
        RejectReparsePoints(fullPath);
        if (encryptedPkcs8.Length is 0 or > 32768
            || !encryptedPkcs8.Span.StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----"u8))
            throw new ArgumentException("Only encrypted private key output is accepted.");
        var snapshot = encryptedPkcs8.ToArray();
        var created = false;
        try
        {
            await using (var stream = CreateProtected(fullPath))
            {
                created = true;
                await stream.WriteAsync(snapshot, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            RejectReparsePoints(fullPath);
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            if (created) File.Delete(fullPath);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(snapshot); }
    }

    public async Task WriteAuthorizationAsync(string path, ReadOnlyMemory<byte> envelope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (envelope.Length is 0 or > 4 * 1024 * 1024) throw new ArgumentException("Output exceeds the permitted size.");
        var fullPath = Path.GetFullPath(path);
        RejectReparsePoints(fullPath);
        RejectPrivateKeyDestination(fullPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var snapshot = envelope.ToArray();
        var temporary = Path.Combine(directory, $".authorization-{Guid.NewGuid():N}.tmp");
        string? backup = null;
        var created = false;
        var completed = false;
        try
        {
            await using (var stream = CreateProtected(temporary))
            {
                await stream.WriteAsync(snapshot, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            RejectReparsePoints(fullPath);
            RejectPrivateKeyDestination(fullPath);
            ct.ThrowIfCancellationRequested();
            if (File.Exists(fullPath))
            {
                backup = Path.Combine(directory, $".authorization-backup-{Guid.NewGuid():N}.tmp");
                File.Replace(temporary, fullPath, backup);
                new FileInfo(backup).SetAccessControl(Security());
            }
            else { File.Move(temporary, fullPath); created = true; }
            new FileInfo(fullPath).SetAccessControl(Security());
            RejectReparsePoints(fullPath);
            ct.ThrowIfCancellationRequested();
            completed = true;
        }
        catch
        {
            if (backup is not null && File.Exists(backup))
            {
                RejectReparsePoints(backup);
                if (File.Exists(fullPath)) File.Replace(backup, fullPath, null);
                else File.Move(backup, fullPath);
                backup = null;
            }
            else if (created) File.Delete(fullPath);
            throw;
        }
        finally
        {
            DeleteScratch(temporary);
            if (completed) DeleteScratch(backup);
            CryptographicOperations.ZeroMemory(snapshot);
        }
    }
    private static FileStream CreateProtected(string path) => new FileInfo(path).Create(
        FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096,
        FileOptions.Asynchronous | FileOptions.WriteThrough, Security());
    private static FileSecurity Security()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new UnauthorizedAccessException("Windows user identity is unavailable.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(owner);
        security.AddAccessRule(new(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
    private static void RejectRepositoryDestination(string path)
    {
        for (var directory = Path.GetDirectoryName(path); directory is not null; directory = Path.GetDirectoryName(directory))
            if (File.Exists(Path.Combine(directory, ".git")) || Directory.Exists(Path.Combine(directory, ".git")))
                throw new ArgumentException("Private keys must be saved outside a Git repository.");
    }
    private static void RejectPrivateKeyDestination(string path)
    {
        try
        {
            using var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read | FileShare.Delete);
            Span<byte> buffer = stackalloc byte[4096];
            var count = stream.Read(buffer);
            ReadOnlySpan<byte> prefix = buffer[..count];
            if (prefix.StartsWith(new byte[] { 0xEF,0xBB,0xBF })) prefix = prefix[3..];
            while (!prefix.IsEmpty && prefix[0] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                prefix = prefix[1..];
            if (prefix.StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----"u8)
                || prefix.StartsWith("-----BEGIN PRIVATE KEY-----"u8)
                || prefix.StartsWith("-----BEGIN EC PRIVATE KEY-----"u8)
                || prefix.StartsWith("-----BEGIN RSA PRIVATE KEY-----"u8)
                || prefix.IsEmpty && count == buffer.Length)
                throw new IOException("Authorization or public key output cannot replace a private key file.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
    private static void RejectReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Reparse destinations are not allowed.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
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

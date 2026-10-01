using System.Security.Cryptography;
using System.Text;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
namespace ProgramMigrationAnalyzer.PublishPreparation;
public static class PublicKeySnapshot
{
    public static string Prepare(string publicKeyPath, string stagingDirectory)
    {
        var bytes = ReadBounded(publicKeyPath);
        var pem = new UTF8Encoding(false, true).GetString(bytes);
        var trust = AuthorizationTrust.FromPublicKeyPem(pem);
        using var key = ECDsa.Create(); key.ImportFromPem(pem);
        var normalized = Encoding.UTF8.GetBytes(key.ExportSubjectPublicKeyInfoPem() + "\n");
        var directory = Path.GetFullPath(stagingDirectory);
        Directory.CreateDirectory(directory);
        for (var ancestor = new DirectoryInfo(directory); ancestor is not null; ancestor = ancestor.Parent)
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe staging path.");
        var snapshot = Path.Combine(directory, trust.KeyId + ".public-key.pem");
        if (File.Exists(snapshot))
        {
            if (!ReadBounded(snapshot).SequenceEqual(normalized)) throw new IOException("Snapshot conflict.");
            return snapshot;
        }
        var created = false;
        try
        {
            using var stream = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            created = true; stream.Write(normalized); stream.Flush(true); return snapshot;
        }
        catch { if (created) File.Delete(snapshot); throw; }
    }
    private static byte[] ReadBounded(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe public key path.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[8193]; var count = 0;
        while (count < bytes.Length)
        {
            var read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count == bytes.Length) throw new IOException("Public key too large.");
        return bytes[..count];
    }
}

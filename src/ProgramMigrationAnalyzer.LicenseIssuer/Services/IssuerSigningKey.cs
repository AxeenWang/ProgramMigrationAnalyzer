using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.LicenseIssuer.Services;

public sealed class IssuerSigningKey : IDisposable
{
    private readonly ECDsa _key;
    private bool _disposed;
    private IssuerSigningKey(ECDsa key) => _key = key;
    public static IssuerSigningKey Generate() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public static IssuerSigningKey ImportEncrypted(ReadOnlySpan<byte> pkcs8, SecureString protectionPassword)
    {
        if (pkcs8.Length is 0 or > 32768) throw new CryptographicException("Invalid encrypted signing key.");
        using var password = ProtectionPassword.Read(protectionPassword);
        var text = new UTF8Encoding(false, true).GetChars(pkcs8.ToArray());
        byte[]? der = null;
        var key = ECDsa.Create();
        try
        {
            if (!PemEncoding.TryFind(text, out var pem) || !text.AsSpan(pem.Label).SequenceEqual("ENCRYPTED PRIVATE KEY")
                || !text.AsSpan(0, pem.Location.Start.GetOffset(text.Length)).Trim().IsEmpty
                || !text.AsSpan(pem.Location.End.GetOffset(text.Length)).Trim().IsEmpty)
                throw new CryptographicException("Only encrypted PKCS8 PEM is accepted.");
            der = Convert.FromBase64String(new string(text.AsSpan(pem.Base64Data)));
            key.ImportEncryptedPkcs8PrivateKey(password.Characters, der, out var consumed);
            if (consumed != der.Length) throw new CryptographicException("Invalid encrypted signing key.");
            _ = AuthorizationTrust.FromPublicKeyPem(key.ExportSubjectPublicKeyInfoPem());
            return new(key);
        }
        catch { key.Dispose(); throw; }
        finally
        {
            if (der is not null) CryptographicOperations.ZeroMemory(der);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(text.AsSpan()));
        }
    }
    public byte[] ExportEncrypted(SecureString protectionPassword)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var password = ProtectionPassword.Read(protectionPassword);
        var der = _key.ExportEncryptedPkcs8PrivateKey(password.Characters,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
        char[]? pem = null;
        try
        {
            pem = PemEncoding.Write("ENCRYPTED PRIVATE KEY", der);
            return Encoding.UTF8.GetBytes(pem);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(der);
            if (pem is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(pem.AsSpan()));
        }
    }
    public string ExportPublicPem()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _key.ExportSubjectPublicKeyInfoPem();
    }
    public byte[] SignPayload(AuthorizationPayload payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = AuthorizationPackageCodec.EncodePayload(payload);
        var keyId = AuthorizationTrust.FromPublicKeyPem(ExportPublicPem()).KeyId!;
        var signature = _key.SignData(AuthorizationPackageCodec.BuildSigningInput(keyId, bytes),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return AuthorizationPackageCodec.EncodeEnvelope(keyId, bytes, signature);
    }
    public override string ToString() => nameof(IssuerSigningKey);
    public void Dispose() { if (_disposed) return; _disposed = true; _key.Dispose(); }
}

internal sealed class ProtectionPassword : IDisposable
{
    private readonly char[] _characters;
    private ProtectionPassword(char[] characters) => _characters = characters;
    internal ReadOnlySpan<char> Characters => _characters;
    internal static ProtectionPassword Read(SecureString password)
    {
        LocalAccountValidation.ValidateNewPassword(password);
        var characters = new char[password.Length];
        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToBSTR(password);
            Marshal.Copy(pointer, characters, 0, characters.Length);
            return new(characters);
        }
        catch { CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan())); throw; }
        finally { if (pointer != IntPtr.Zero) Marshal.ZeroFreeBSTR(pointer); }
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(_characters.AsSpan()));
}

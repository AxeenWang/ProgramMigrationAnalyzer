using System.Security.Cryptography;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public sealed class AuthorizationTrust
{
    private readonly byte[]? _spki;
    private AuthorizationTrust(byte[]? spki)
    {
        _spki = spki?.ToArray();
        KeyId = spki is null ? null : Convert.ToHexString(SHA256.HashData(spki));
    }
    public static AuthorizationTrust None { get; } = new(null);
    public string? KeyId { get; }
    public static AuthorizationTrust FromPublicKeyPem(ReadOnlySpan<char> pem)
    {
        try
        {
            if (pem.Length > 8192 || !PemEncoding.TryFind(pem, out var fields)
                || !pem[fields.Label].SequenceEqual("PUBLIC KEY")
                || !pem[..fields.Location.Start.GetOffset(pem.Length)].Trim().IsEmpty
                || !pem[fields.Location.End.GetOffset(pem.Length)..].Trim().IsEmpty)
                throw InvalidTrust();
            var spki = Convert.FromBase64String(pem[fields.Base64Data].ToString());
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(spki, out var consumed);
            if (consumed != spki.Length || key.KeySize != 256
                || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") throw InvalidTrust();
            return new(spki);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or CryptographicException)
        { throw InvalidTrust(); }
    }
    internal ECDsa CreateVerifier()
    {
        if (_spki is null) throw InvalidTrust();
        var key = ECDsa.Create();
        try { key.ImportSubjectPublicKeyInfo(_spki, out _); return key; }
        catch { key.Dispose(); throw; }
    }
    private static AuthorizationException InvalidTrust() => new(AuthorizationFailure.UntrustedKey);
    public override string ToString() => nameof(AuthorizationTrust);
}

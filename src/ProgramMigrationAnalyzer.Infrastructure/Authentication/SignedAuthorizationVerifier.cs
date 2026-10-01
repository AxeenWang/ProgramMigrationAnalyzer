using System.Security.Cryptography;
using System.Text.Json;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public sealed class SignedAuthorizationVerifier
{
    private readonly AuthorizationTrust _trust;
    public SignedAuthorizationVerifier(AuthorizationTrust trust) => _trust = trust ?? throw new ArgumentNullException(nameof(trust));
    public VerifiedAuthorization Verify(ReadOnlyMemory<byte> envelope)
    {
        try
        {
            if (envelope.Length > AuthorizationPackageCodec.MaximumFileBytes) throw AuthorizationPackageCodec.InvalidData();
            var snapshot = envelope.ToArray();
            using var document = AuthorizationPackageCodec.ParseJson(snapshot);
            var root = document.RootElement;
            AuthorizationPackageCodec.RequireProperties(root, "formatVersion", "algorithm", "keyId", "payload", "signature");
            if (root.GetProperty("formatVersion").GetInt32() != 1
                || AuthorizationPackageCodec.RequiredString(root, "algorithm") != AuthorizationPackageCodec.Algorithm) throw AuthorizationPackageCodec.InvalidData();
            var keyId = AuthorizationPackageCodec.RequiredString(root, "keyId");
            AuthorizationPackageCodec.ValidateKeyId(keyId);
            if (keyId != _trust.KeyId) throw new AuthorizationException(AuthorizationFailure.UntrustedKey);
            var payload = AuthorizationPackageCodec.DecodeBase64(AuthorizationPackageCodec.RequiredString(root, "payload"), AuthorizationPackageCodec.MaximumFileBytes);
            var signature = AuthorizationPackageCodec.DecodeBase64(AuthorizationPackageCodec.RequiredString(root, "signature"), 64);
            if (signature.Length != 64) throw AuthorizationPackageCodec.InvalidData();
            using var verifier = _trust.CreateVerifier();
            if (!verifier.VerifyData(AuthorizationPackageCodec.BuildSigningInput(keyId, payload), signature,
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new AuthorizationException(AuthorizationFailure.InvalidSignature);
            var accounts = AuthorizationPackageCodec.ReadPayload(payload);
            return new(accounts, keyId, snapshot, payload);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException
            or InvalidOperationException or OverflowException or CryptographicException)
        { throw AuthorizationPackageCodec.InvalidData(); }
    }
    public async Task<VerifiedAuthorization> ReadAndVerifyAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        var total = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, AuthorizationPackageCodec.MaximumFileBytes + 1 - total)), ct);
            if (count == 0) break;
            total += count;
            if (total > AuthorizationPackageCodec.MaximumFileBytes) throw AuthorizationPackageCodec.InvalidData();
            bytes.Write(buffer, 0, count);
        }
        ct.ThrowIfCancellationRequested();
        return Verify(bytes.GetBuffer().AsMemory(0, total));
    }
}

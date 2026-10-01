using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal sealed class AuthorizationTestFixture : IDisposable
{
    internal readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal string PublicPem => Key.ExportSubjectPublicKeyInfoPem();
    internal string KeyId => Convert.ToHexString(SHA256.HashData(Key.ExportSubjectPublicKeyInfo()));
    internal SignedAuthorizationVerifier Verifier => new(AuthorizationTrust.FromPublicKeyPem(PublicPem));
    internal JsonObject Payload(int revision = 1) => new()
    {
        ["schemaVersion"] = 2, ["productId"] = "ProgramMigrationAnalyzer",
        ["authorizationId"] = "d6b21b0a-cf2a-4503-9c2b-c6069e1ec9a6", ["revision"] = revision,
        ["issuedAtUtc"] = "2026-10-01T00:00:00Z",
        ["users"] = new JsonArray(new JsonObject
        {
            ["userId"] = "a1b3cfd1-51d5-434e-8419-0245446b1556", ["username"] = "internal.one",
            ["displayName"] = "Internal One", ["isEnabled"] = true,
            ["passwordAlgorithm"] = "PBKDF2-HMAC-SHA256", ["iterations"] = 600_000,
            ["salt"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            ["passwordHash"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        })
    };
    internal byte[] Issue(JsonObject? payload = null) => Sign(Encoding.UTF8.GetBytes((payload ?? Payload()).ToJsonString()));
    internal byte[] Sign(byte[] payload)
    {
        // Independent fixture: literals do not use production signing-input or serialization helpers.
        var header = Encoding.UTF8.GetBytes($"PMA-OFFLINE-AUTH/v1\nECDSA-P256-SHA256-P1363\n{KeyId}\n");
        var signature = Key.SignData(header.Concat(payload).ToArray(), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, algorithm = "ECDSA-P256-SHA256-P1363", keyId = KeyId,
            payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(signature)
        });
    }
    public void Dispose() => Key.Dispose();
}

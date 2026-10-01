namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public sealed record AuthorizationPayload(int SchemaVersion, string ProductId, Guid AuthorizationId,
    int Revision, DateTimeOffset IssuedAtUtc, IReadOnlyList<LocalAccountRecord> Users)
{
    public override string ToString() => nameof(AuthorizationPayload);
}

public sealed class VerifiedAuthorization
{
    private readonly byte[] _envelope;
    private readonly byte[] _payload;
    internal VerifiedAuthorization(AuthorizationPayload payload, string keyId, byte[] envelope, byte[] payloadBytes)
    {
        Payload = payload;
        KeyId = keyId;
        _envelope = envelope.ToArray();
        _payload = payloadBytes.ToArray();
        PayloadFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_payload));
    }
    public AuthorizationPayload Payload { get; }
    public string KeyId { get; }
    public string PayloadFingerprint { get; }
    public byte[] CopyEnvelopeBytes() => _envelope.ToArray();
    public byte[] CopyPayloadBytes() => _payload.ToArray();
    public override string ToString() => nameof(VerifiedAuthorization);
}

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class AuthorizationSignatureChecks
{
    public static void Run() => CheckSupport.Run(
        (nameof(ValidCompanySignature), ValidCompanySignature),
        (nameof(EveryProtectedFieldTamperIsRejected), EveryProtectedFieldTamperIsRejected),
        (nameof(WrongKeyAlgorithmProductAndSchema), WrongKeyAlgorithmProductAndSchema),
        (nameof(DuplicateUnknownAndTrailingData), DuplicateUnknownAndTrailingData),
        (nameof(GrowingStreamIsBounded), GrowingStreamIsBounded),
        (nameof(TrustRejectsPrivatePemAndOtherCurves), TrustRejectsPrivatePemAndOtherCurves),
        (nameof(PayloadBoundsAndNormalization), PayloadBoundsAndNormalization),
        (nameof(CodecUsesInteroperableSigningBytes), CodecUsesInteroperableSigningBytes),
        (nameof(CodecRejectsInvalidUnicodeInput), CodecRejectsInvalidUnicodeInput));

    private static void Reject(SignedAuthorizationVerifier verifier, byte[] bytes) =>
        CheckSupport.Throws<AuthorizationException>(() => verifier.Verify(bytes));

    private static void ValidCompanySignature()
    {
        using var fixture = new AuthorizationTestFixture();
        var envelope = fixture.Issue();
        var verified = fixture.Verifier.Verify(envelope);
        CheckSupport.Check(verified.Payload.SchemaVersion == 2 && verified.Payload.Revision == 1
            && verified.Payload.Users.Count == 1 && verified.Payload.Users[0].Username == "internal.one", "Valid signed accounts were not read.");
        var copy = verified.CopyEnvelopeBytes(); copy[0] = 0;
        CheckSupport.Check(verified.CopyEnvelopeBytes().SequenceEqual(envelope), "Verified envelope exposes mutable storage.");
        CheckSupport.Check(!verified.ToString().Contains("internal.one", StringComparison.Ordinal), "Verified value leaks account details.");
        Reject(new SignedAuthorizationVerifier(AuthorizationTrust.None), envelope);
    }
    private static void EveryProtectedFieldTamperIsRejected()
    {
        using var fixture = new AuthorizationTestFixture();
        var original = fixture.Issue();
        foreach (var field in new[] { "username", "displayName", "isEnabled", "salt", "passwordHash", "iterations", "revision" })
        {
            var outer = JsonNode.Parse(original)!.AsObject();
            var payload = JsonNode.Parse(Convert.FromBase64String(outer["payload"]!.GetValue<string>()))!.AsObject();
            var owner = field == "revision" ? payload : payload["users"]![0]!.AsObject();
            owner[field] = field switch { "isEnabled" => JsonValue.Create(false), "iterations" => JsonValue.Create(600_001),
                "revision" => JsonValue.Create(2), _ => JsonValue.Create("tampered") };
            outer["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToJsonString()));
            Reject(fixture.Verifier, Encoding.UTF8.GetBytes(outer.ToJsonString()));
        }
        foreach (var field in new[] { "signature", "keyId", "algorithm", "payload" })
        {
            var outer = JsonNode.Parse(original)!.AsObject(); outer[field] = "tampered";
            Reject(fixture.Verifier, Encoding.UTF8.GetBytes(outer.ToJsonString()));
        }
    }
    private static void WrongKeyAlgorithmProductAndSchema()
    {
        using var fixture = new AuthorizationTestFixture(); using var other = new AuthorizationTestFixture();
        Reject(fixture.Verifier, other.Issue());
        foreach (var (field, value) in new (string, JsonNode?)[] { ("productId", JsonValue.Create("OtherProduct")),
                     ("schemaVersion", JsonValue.Create(1)), ("issuedAtUtc", JsonValue.Create("2026-10-01T00:00:00+08:00")) })
        { var payload = fixture.Payload(); payload[field] = value; Reject(fixture.Verifier, fixture.Issue(payload)); }
        Reject(fixture.Verifier, Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"users\":[]}"));
    }
    private static void DuplicateUnknownAndTrailingData()
    {
        using var fixture = new AuthorizationTestFixture();
        var original = fixture.Issue(); var text = Encoding.UTF8.GetString(original);
        Reject(fixture.Verifier, Encoding.UTF8.GetBytes(text[..^1] + ",\"formatVersion\":1}"));
        Reject(fixture.Verifier, Encoding.UTF8.GetBytes(text[..^1] + ",\"extra\":true}"));
        Reject(fixture.Verifier, Encoding.UTF8.GetBytes(text + "{}"));
        var payload = fixture.Payload().ToJsonString();
        Reject(fixture.Verifier, fixture.Sign(Encoding.UTF8.GetBytes(payload[..^1] + ",\"revision\":1}")));
        Reject(fixture.Verifier, fixture.Sign(Encoding.UTF8.GetBytes(payload[..^1] + ",\"extra\":true}")));
        Reject(fixture.Verifier, fixture.Sign([0xC3, 0x28]));
        var outer = JsonNode.Parse(original)!.AsObject(); outer["payload"] = " " + outer["payload"]!.GetValue<string>();
        Reject(fixture.Verifier, Encoding.UTF8.GetBytes(outer.ToJsonString()));
        var nested = fixture.Payload(); nested["extra"] = JsonNode.Parse(new string('[', 17) + "0" + new string(']', 17));
        Reject(fixture.Verifier, fixture.Issue(nested));
    }
    private static void GrowingStreamIsBounded()
    {
        using var fixture = new AuthorizationTestFixture(); using var stream = new EndlessReadStream();
        CheckSupport.Throws<AuthorizationException>(() => fixture.Verifier.ReadAndVerifyAsync(stream).GetAwaiter().GetResult());
        CheckSupport.Check(stream.BytesRead <= 4 * 1024 * 1024 + 1, "Growing stream was read beyond the bound.");
    }
    private static void TrustRejectsPrivatePemAndOtherCurves()
    {
        CheckSupport.Throws<AuthorizationException>(() => AuthorizationTrust.FromPublicKeyPem("-----BEGIN PRIVATE KEY-----\nAA==\n-----END PRIVATE KEY-----"));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        CheckSupport.Throws<AuthorizationException>(() => AuthorizationTrust.FromPublicKeyPem(key.ExportSubjectPublicKeyInfoPem()));
        using var fixture = new AuthorizationTestFixture();
        var der = fixture.Key.ExportSubjectPublicKeyInfo().Concat(new byte[] { 0 }).ToArray();
        var pem = PemEncoding.WriteString("PUBLIC KEY", der);
        CheckSupport.Throws<AuthorizationException>(() => AuthorizationTrust.FromPublicKeyPem(pem));
    }
    private static void PayloadBoundsAndNormalization()
    {
        using var fixture = new AuthorizationTestFixture();
        foreach (var (field, value) in new (string, JsonNode?)[] { ("username", JsonValue.Create("Internal.One")),
            ("iterations", JsonValue.Create(599_999)), ("iterations", JsonValue.Create(2_000_001)),
            ("salt", JsonValue.Create(Convert.ToBase64String(new byte[17]))),
            ("passwordHash", JsonValue.Create(Convert.ToBase64String(new byte[31]))),
            ("displayName", JsonValue.Create(new string('a',129))), ("userId", JsonValue.Create(Guid.Empty.ToString())) })
        { var p = fixture.Payload(); p["users"]![0]![field] = value; Reject(fixture.Verifier, fixture.Issue(p)); }
        var duplicate = fixture.Payload(); duplicate["users"]!.AsArray().Add(duplicate["users"]![0]!.DeepClone());
        Reject(fixture.Verifier, fixture.Issue(duplicate));
        foreach (var revision in new[] { 0, -1 }) Reject(fixture.Verifier, fixture.Issue(fixture.Payload(revision)));
        foreach (var iterations in new[] { 600_000, 2_000_000 })
        { var p = fixture.Payload(int.MaxValue); p["users"]![0]!["iterations"] = iterations; fixture.Verifier.Verify(fixture.Issue(p)); }
        var empty = fixture.Payload(); empty["users"] = new JsonArray(); Reject(fixture.Verifier, fixture.Issue(empty));
        var tooMany = fixture.Payload(); var users = tooMany["users"]!.AsArray();
        while (users.Count < 1001) users.Add(users[0]!.DeepClone());
        Reject(fixture.Verifier, fixture.Issue(tooMany));
    }
    private static void CodecUsesInteroperableSigningBytes()
    {
        using var fixture = new AuthorizationTestFixture(); var verified = fixture.Verifier.Verify(fixture.Issue());
        var payload = AuthorizationPackageCodec.EncodePayload(verified.Payload);
        var input = AuthorizationPackageCodec.BuildSigningInput(fixture.KeyId, payload);
        var expectedHeader = Encoding.UTF8.GetBytes($"PMA-OFFLINE-AUTH/v1\nECDSA-P256-SHA256-P1363\n{fixture.KeyId}\n");
        CheckSupport.Check(input.SequenceEqual(expectedHeader.Concat(payload)), "Signing domain bytes differ from protocol.");
        var signature = fixture.Key.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        fixture.Verifier.Verify(AuthorizationPackageCodec.EncodeEnvelope(fixture.KeyId, payload, signature));
    }
    private static void CodecRejectsInvalidUnicodeInput()
    {
        using var fixture = new AuthorizationTestFixture(); var original = fixture.Verifier.Verify(fixture.Issue()).Payload;
        var invalid = original with { Users = new[] { original.Users[0] with { DisplayName = "\uD800" } } };
        CheckSupport.Throws<AuthorizationException>(() => AuthorizationPackageCodec.EncodePayload(invalid));
    }
    private sealed class EndlessReadStream : Stream
    {
        internal int BytesRead;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)' ', offset, count); BytesRead += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); buffer.Span.Fill((byte)' '); BytesRead += buffer.Length; return ValueTask.FromResult(buffer.Length); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

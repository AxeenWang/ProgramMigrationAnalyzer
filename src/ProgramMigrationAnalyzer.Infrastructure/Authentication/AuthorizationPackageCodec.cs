using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public static class AuthorizationPackageCodec
{
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    internal const string Algorithm = "ECDSA-P256-SHA256-P1363";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] EncodePayload(AuthorizationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Users is null) throw InvalidData();
        foreach (var user in payload.Users)
        {
            if (user is null || user.DisplayName is null) throw InvalidData();
            // Reject malformed UTF-16 before the serializer can replace it.
            ValidateDisplayName(user.DisplayName);
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        ReadPayload(bytes);
        return bytes;
    }
    public static byte[] BuildSigningInput(string keyId, ReadOnlySpan<byte> payload)
    {
        ValidateKeyId(keyId);
        if (payload.Length > MaximumFileBytes) throw InvalidData();
        var header = Encoding.UTF8.GetBytes($"PMA-OFFLINE-AUTH/v1\n{Algorithm}\n{keyId}\n");
        var bytes = new byte[checked(header.Length + payload.Length)];
        header.CopyTo(bytes, 0);
        payload.CopyTo(bytes.AsSpan(header.Length));
        return bytes;
    }
    public static byte[] EncodeEnvelope(string keyId, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        ValidateKeyId(keyId);
        if (signature.Length != 64 || payload.Length > MaximumFileBytes) throw InvalidData();
        ReadPayload(payload.ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, algorithm = Algorithm, keyId,
            payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(signature)
        });
        if (bytes.Length > MaximumFileBytes) throw InvalidData();
        return bytes;
    }
    internal static JsonDocument ParseJson(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumFileBytes) throw InvalidData();
        StrictUtf8.GetCharCount(bytes.Span);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
    }
    internal static AuthorizationPayload ReadPayload(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = ParseJson(bytes);
            var root = document.RootElement;
            RequireProperties(root, "schemaVersion", "productId", "authorizationId", "revision", "issuedAtUtc", "users");
            var schema = root.GetProperty("schemaVersion").GetInt32();
            var product = RequiredString(root, "productId");
            var id = root.GetProperty("authorizationId").GetGuid();
            var revision = root.GetProperty("revision").GetInt32();
            var issuedElement = root.GetProperty("issuedAtUtc");
            var issuedText = issuedElement.GetString();
            if (schema != 2 || product != "ProgramMigrationAnalyzer" || id == Guid.Empty || revision < 1
                || issuedText is null || issuedText.Length > 40
                || !(issuedText.EndsWith('Z') || issuedText.EndsWith("+00:00", StringComparison.Ordinal))
                || !issuedElement.TryGetDateTimeOffset(out var issued) || issued.Offset != TimeSpan.Zero) throw InvalidData();
            var userElements = root.GetProperty("users");
            if (userElements.ValueKind != JsonValueKind.Array || userElements.GetArrayLength() is < 1 or > 1000) throw InvalidData();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<Guid>();
            var users = new List<LocalAccountRecord>();
            foreach (var user in userElements.EnumerateArray())
            {
                RequireProperties(user, "userId", "username", "displayName", "isEnabled", "passwordAlgorithm", "iterations", "salt", "passwordHash");
                var userId = user.GetProperty("userId").GetGuid();
                var username = RequiredString(user, "username");
                var display = RequiredString(user, "displayName");
                if (userId == Guid.Empty || !ids.Add(userId) || username != LocalAccountValidation.NormalizeUsername(username)
                    || !names.Add(username) || string.IsNullOrWhiteSpace(display)) throw InvalidData();
                ValidateDisplayName(display);
                var algorithm = RequiredString(user, "passwordAlgorithm");
                var iterations = user.GetProperty("iterations").GetInt32();
                var saltText = RequiredString(user, "salt");
                var hashText = RequiredString(user, "passwordHash");
                var salt = DecodeBase64(saltText, 16);
                var hash = DecodeBase64(hashText, 32);
                if (salt.Length != 16 || hash.Length != 32) throw InvalidData();
                LocalPasswordHasher.ValidateRecord(new(algorithm, iterations, salt, hash));
                users.Add(new(userId, username, display, user.GetProperty("isEnabled").GetBoolean(), algorithm, iterations, saltText, hashText));
            }
            return new(schema, product, id, revision, issued, users.AsReadOnly());
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException
            or InvalidOperationException or OverflowException)
        { throw InvalidData(); }
    }
    internal static void ValidateDisplayName(string display)
    {
        ReadOnlySpan<char> rest = display;
        var count = 0;
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out _, out var consumed) != OperationStatus.Done || ++count > 128) throw InvalidData();
            rest = rest[consumed..];
        }
        if (count == 0 || string.IsNullOrWhiteSpace(display)) throw InvalidData();
    }
    internal static void RequireProperties(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidData();
        var names = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != required.Length || names.Distinct(StringComparer.Ordinal).Count() != required.Length
            || required.Any(name => !names.Contains(name, StringComparer.Ordinal))) throw InvalidData();
    }
    internal static string RequiredString(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw InvalidData();
    internal static byte[] DecodeBase64(string text, int maximumBytes)
    {
        if (text.Length > 4 * ((maximumBytes + 2) / 3)) throw InvalidData();
        var bytes = Convert.FromBase64String(text);
        if (bytes.Length > maximumBytes || Convert.ToBase64String(bytes) != text) throw InvalidData();
        return bytes;
    }
    internal static void ValidateKeyId(string id)
    {
        if (id is null || id.Length != 64 || id.Any(value => value is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))) throw InvalidData();
    }
    internal static AuthorizationException InvalidData() => new(AuthorizationFailure.InvalidData);
}

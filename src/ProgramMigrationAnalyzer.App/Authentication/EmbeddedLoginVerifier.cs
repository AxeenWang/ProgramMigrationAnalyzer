using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProgramMigrationAnalyzer.App.Authentication;

public sealed class EmbeddedLoginVerifier : ILoginVerifier
{
    private const string ResourceName = "ProgramMigrationAnalyzer.EmbeddedLogin.json";
    private readonly string username;
    private readonly byte[] salt;
    private readonly byte[] hash;

    private EmbeddedLoginVerifier(string username, byte[] salt, byte[] hash)
    {
        this.username = username;
        this.salt = salt;
        this.hash = hash;
    }

    public static EmbeddedLoginVerifier FromEmbeddedResource()
    {
        using var stream = typeof(EmbeddedLoginVerifier).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw InvalidConfiguration();
        return FromStream(stream);
    }

    public static EmbeddedLoginVerifier FromStream(Stream stream)
    {
        try
        {
            var bytes = new byte[4097];
            var size = 0;
            int read;
            while (size < bytes.Length && (read = stream.Read(bytes, size, bytes.Length - size)) > 0) size += read;
            if (size == 0 || size > 4096) throw InvalidConfiguration();
            using var document = JsonDocument.Parse(bytes.AsMemory(0, size), new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw InvalidConfiguration();
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!properties.Add(property.Name)) throw InvalidConfiguration();
            if (!properties.SetEquals(["schemaVersion", "username", "algorithm", "iterations", "salt", "hash"]) ||
                root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("algorithm").GetString() != "PBKDF2-HMAC-SHA256" ||
                root.GetProperty("iterations").GetInt32() != 600_000) throw InvalidConfiguration();
            var username = root.GetProperty("username").GetString();
            var salt = root.GetProperty("salt").GetBytesFromBase64();
            var hash = root.GetProperty("hash").GetBytesFromBase64();
            if (string.IsNullOrWhiteSpace(username) || username.Length > 128 || salt.Length != 16 || hash.Length != 32)
                throw InvalidConfiguration();
            return new EmbeddedLoginVerifier(username, salt, hash);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException
                                         or KeyNotFoundException or ArgumentException or OverflowException)
        {
            throw InvalidConfiguration();
        }
    }

    public bool Verify(string username, string password)
    {
        if (string.IsNullOrEmpty(username) || username.Length > 128 || string.IsNullOrEmpty(password) || password.Length > 128)
            return false;
        byte[] bytes;
        try { bytes = new UTF8Encoding(false, true).GetBytes(password); }
        catch (EncoderFallbackException) { return false; }
        byte[]? candidate = null;
        try
        {
            candidate = Rfc2898DeriveBytes.Pbkdf2(bytes, salt, 600_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(candidate, hash) & string.Equals(username, this.username, StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (candidate is not null) CryptographicOperations.ZeroMemory(candidate);
        }
    }

    private static InvalidDataException InvalidConfiguration() => new("Embedded login configuration is unavailable or invalid.");
}

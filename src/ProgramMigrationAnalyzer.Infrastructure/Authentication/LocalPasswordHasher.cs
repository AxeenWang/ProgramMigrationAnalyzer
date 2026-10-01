using System.Security;
using System.Security.Cryptography;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public sealed class LocalPasswordHasher
{
    public const string Algorithm = "PBKDF2-HMAC-SHA256";
    public const int CreationIterations = 600_000;
    public const int MaximumIterations = 2_000_000;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public PasswordHashRecord Create(SecureString password)
    {
        using var buffer = PasswordBuffer.Read(password, minimumScalarCount: 15);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(buffer.Bytes, salt, CreationIterations, HashAlgorithmName.SHA256, HashLength);
        return new PasswordHashRecord(Algorithm, CreationIterations, salt, hash);
    }

    public bool Verify(SecureString password, PasswordHashRecord record)
    {
        ValidateRecord(record);
        ArgumentNullException.ThrowIfNull(password);
        PasswordBuffer buffer;
        try
        {
            buffer = PasswordBuffer.Read(password);
        }
        catch (ArgumentException)
        {
            return false;
        }

        using (buffer)
        {
            Span<byte> candidate = stackalloc byte[HashLength];
            try
            {
                Rfc2898DeriveBytes.Pbkdf2(buffer.Bytes, record.Salt, candidate, record.Iterations, HashAlgorithmName.SHA256);
                return CryptographicOperations.FixedTimeEquals(candidate, record.Hash);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(candidate);
            }
        }
    }

    internal static void ValidateRecord(PasswordHashRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Algorithm != Algorithm || record.Iterations is < CreationIterations or > MaximumIterations
            || record.Salt is null || record.Salt.Length < SaltLength
            || record.Hash is null || record.Hash.Length != HashLength)
        {
            throw new ArgumentException("Stored password parameters are invalid or unsupported.", nameof(record));
        }
    }
}

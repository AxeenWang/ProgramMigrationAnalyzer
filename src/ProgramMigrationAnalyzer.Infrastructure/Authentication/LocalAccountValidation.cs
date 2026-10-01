using System.Buffers;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public static class LocalAccountValidation
{
    public static string NormalizeUsername(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim();
        if (trimmed.Length is < 3 or > 64
            || trimmed.Any(character => character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9') and not '.' and not '_' and not '-'))
        {
            throw new ArgumentException("Username must contain 3 to 64 ASCII letters, digits, dots, underscores or hyphens.", nameof(value));
        }

        return trimmed.ToLowerInvariant();
    }

    public static void ValidateNewPassword(SecureString password)
    {
        using var buffer = PasswordBuffer.Read(password, minimumScalarCount: 8);
    }
}

// No managed plaintext string is created. Both conversion buffers and the length-prefixed
// BSTR are cleared, including input after an embedded NUL.
internal sealed class PasswordBuffer : IDisposable
{
    private readonly byte[] _bytes;
    private bool _disposed;

    private PasswordBuffer(byte[] bytes) => _bytes = bytes;

    public ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bytes;
        }
    }

    public static PasswordBuffer Read(SecureString password, int minimumScalarCount = 0)
    {
        ArgumentNullException.ThrowIfNull(password);
        var length = password.Length;
        if (length > 256)
        {
            throw InvalidPassword();
        }

        var characters = new char[length];
        var pointer = IntPtr.Zero;
        byte[]? encoded = null;
        try
        {
            pointer = Marshal.SecureStringToBSTR(password);
            Marshal.Copy(pointer, characters, 0, characters.Length);
            ReadOnlySpan<char> remaining = characters;
            var scalarCount = 0;
            while (!remaining.IsEmpty)
            {
                if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done)
                {
                    throw InvalidPassword();
                }

                scalarCount++;
                remaining = remaining[consumed..];
            }

            if (scalarCount < minimumScalarCount || scalarCount > 128)
            {
                throw InvalidPassword();
            }

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            encoded = new byte[utf8.GetByteCount(characters)];
            utf8.GetBytes(characters.AsSpan(), encoded.AsSpan());
            var result = new PasswordBuffer(encoded);
            encoded = null; // Ownership transfers to the disposable buffer.
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
            if (encoded is not null)
            {
                CryptographicOperations.ZeroMemory(encoded);
            }

            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(pointer);
            }
        }
    }

    private static ArgumentException InvalidPassword() =>
        new("Password must contain valid Unicode and meet the permitted length.", "password");

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_bytes);
            _disposed = true;
        }
    }
}

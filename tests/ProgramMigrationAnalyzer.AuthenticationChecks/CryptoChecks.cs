using System.Text.Json;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class CryptoChecks
{
    public static void Run() => CheckSupport.Run(
        (nameof(UsernameCanonicalization), UsernameCanonicalization),
        (nameof(PasswordUnicodeAndWhitespace), PasswordUnicodeAndWhitespace),
        (nameof(EightCharacterPasswordWithSymbols), EightCharacterPasswordWithSymbols),
        (nameof(SaltAndResetSemantics), SaltAndResetSemantics),
        (nameof(HashParametersRejectedBeforeExpensiveWork), HashParametersRejectedBeforeExpensiveWork),
        (nameof(IndependentPbkdf2Vectors), IndependentPbkdf2Vectors),
        (nameof(InvalidPasswordCannotVerify), InvalidPasswordCannotVerify),
        (nameof(AccountModelsRoundTripWithoutPrintingHash), AccountModelsRoundTripWithoutPrintingHash));

    private static void UsernameCanonicalization()
    {
        Check(LocalAccountValidation.NormalizeUsername(" User.One ") == "user.one", "Username must trim and use invariant lower case.");
        Check(LocalAccountValidation.NormalizeUsername("A_0-z") == "a_0-z", "Allowed ASCII punctuation must survive normalization.");
        Check(LocalAccountValidation.NormalizeUsername(new string('A', 64)) == new string('a', 64), "64-character username must be accepted.");
        foreach (var invalid in new[] { "ab", new string('a', 65), "user/name", "使用者", "user name", "user\0name", "Kelvin" })
        {
            Throws<ArgumentException>(() => LocalAccountValidation.NormalizeUsername(invalid));
        }

        Throws<ArgumentNullException>(() => LocalAccountValidation.NormalizeUsername(null!));
    }

    private static void PasswordUnicodeAndWhitespace()
    {
        foreach (var value in new[] { new string('a', 8), string.Concat(Enumerable.Repeat("😀", 8)), new string('a', 128), string.Concat(Enumerable.Repeat("😀", 128)), "  Mixed Case 密碼😀  " })
        {
            using var password = Password(value);
            LocalAccountValidation.ValidateNewPassword(password);
        }

        foreach (var value in new[] { new string('a', 7), new string('a', 129), string.Concat(Enumerable.Repeat("😀", 7)), string.Concat(Enumerable.Repeat("😀", 129)), "abcdefghijklmno\uD800", "abcdefghijklmno\uDC00" })
        {
            using var password = Password(value);
            Throws<ArgumentException>(() => LocalAccountValidation.ValidateNewPassword(password));
        }

        using var exact = Password("  Mixed Case 密碼😀  ");
        using var trimmed = Password("Mixed Case 密碼😀");
        using var lower = Password("  mixed case 密碼😀  ");
        var hasher = new LocalPasswordHasher();
        var hash = hasher.Create(exact);
        Check(hasher.Verify(exact, hash) && !hasher.Verify(trimmed, hash) && !hasher.Verify(lower, hash), "Password whitespace, Unicode and case must be preserved.");
        using var composed = Password("Test-only password é");
        using var decomposed = Password("Test-only password e\u0301");
        Check(!hasher.Verify(decomposed, hasher.Create(composed)), "Password Unicode must not be normalized.");
    }

    private static void EightCharacterPasswordWithSymbols()
    {
        using var exact = Password("Ab@!#9x?");
        using var changedSymbol = Password("Ab@!#9x!");
        using var changedCase = Password("ab@!#9x?");
        using var tooShort = Password("Ab@!#9x");
        var hasher = new LocalPasswordHasher();
        var record = hasher.Create(exact);
        Check(hasher.Verify(exact, record) && !hasher.Verify(changedSymbol, record)
            && !hasher.Verify(changedCase, record), "Eight-character passwords must preserve symbols and case exactly.");
        Throws<ArgumentException>(() => hasher.Create(tooShort));
    }

    private static void SaltAndResetSemantics()
    {
        using var oldPassword = Password("Old test-only credential");
        using var newPassword = Password("New test-only credential");
        var hasher = new LocalPasswordHasher();
        var first = hasher.Create(oldPassword);
        var second = hasher.Create(oldPassword);
        var reset = hasher.Create(newPassword);
        Check(first.Algorithm == "PBKDF2-HMAC-SHA256" && first.Iterations == 600_000 && first.Salt.Length >= 16 && first.Hash.Length == 32,
            "New hashes must use the required work factor, algorithm and lengths.");
        Check(!first.Salt.SequenceEqual(second.Salt) && !first.Hash.SequenceEqual(second.Hash), "Every creation must use a fresh random salt.");
        Check(hasher.Verify(oldPassword, first) && hasher.Verify(oldPassword, second) && !hasher.Verify(newPassword, first), "Only the exact original password must verify.");
        Check(hasher.Verify(newPassword, reset) && !hasher.Verify(oldPassword, reset), "Reset hash must reject the old password.");
    }

    private static void HashParametersRejectedBeforeExpensiveWork()
    {
        var hasher = new LocalPasswordHasher();
        var valid = new PasswordHashRecord("PBKDF2-HMAC-SHA256", 600_000, new byte[16], new byte[32]);
        var disposedPassword = Password("Disposed test-only credential");
        disposedPassword.Dispose();
        foreach (var record in new[]
        {
            valid with { Algorithm = "unknown-test-algorithm" },
            valid with { Iterations = 599_999 },
            valid with { Iterations = 2_000_001 },
            valid with { Salt = new byte[15] },
            valid with { Salt = null! },
            valid with { Hash = new byte[31] },
            valid with { Hash = new byte[33] },
            valid with { Hash = null! }
        })
        {
            // Invalid stored parameters must fail before even reading the credential.
            Throws<ArgumentException>(() => hasher.Verify(disposedPassword, record));
        }

        Throws<ArgumentNullException>(() => hasher.Verify(disposedPassword, null!));
    }

    private static void IndependentPbkdf2Vectors()
    {
        // Test-only vectors generated independently with Python hashlib.pbkdf2_hmac.
        // Includes leading/trailing spaces, non-ASCII, a surrogate pair and an embedded NUL.
        using var password = Password("  Test-Only 密碼😀\0  ");
        using var changedAfterNul = Password("  Test-Only 密碼😀\0 x");
        var salt = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var hasher = new LocalPasswordHasher();
        foreach (var (iterations, expected) in new[]
        {
            (600_000, "bdbaa9bbaa7950cbcab81baf9df6997aec58d3c8ff684dfbb6effdffb9271cbc"),
            (2_000_000, "a989115782d76940d46585632de177baf3cd1b617706b0fa4aa98ee816f3d9f4")
        })
        {
            var record = new PasswordHashRecord("PBKDF2-HMAC-SHA256", iterations, salt, Convert.FromHexString(expected));
            Check(hasher.Verify(password, record) && !hasher.Verify(changedAfterNul, record), "PBKDF2 must match the independent UTF-8 vector including all input after NUL.");
        }
    }

    private static void InvalidPasswordCannotVerify()
    {
        using var original = Password("Valid test-only credential");
        var hasher = new LocalPasswordHasher();
        var record = hasher.Create(original);
        foreach (var value in new[] { string.Empty, new string('x', 129), "test-only invalid\uD800" })
        {
            using var candidate = Password(value);
            Check(!hasher.Verify(candidate, record), "Invalid password input must not verify.");
        }
    }

    private static void AccountModelsRoundTripWithoutPrintingHash()
    {
        const string hashFixture = "Test-only hash marker";
        const string saltFixture = "Test-only salt marker";
        var user = new LocalAccountRecord(Guid.NewGuid(), "test.user", "Test user", true, "PBKDF2-HMAC-SHA256", 600_000, saltFixture, hashFixture);
        var accounts = new LocalAccountFile(1, [user]);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(accounts, options);
        var restored = JsonSerializer.Deserialize<LocalAccountFile>(json, options);
        Check(restored?.SchemaVersion == 1 && restored.Users.Single() == user, "Account data must round-trip with the schema 1 fields.");
        using var document = JsonDocument.Parse(json);
        var serializedUser = document.RootElement.GetProperty("users")[0];
        Check(serializedUser.GetProperty("passwordAlgorithm").GetString() == "PBKDF2-HMAC-SHA256"
            && serializedUser.GetProperty("passwordHash").GetString() == hashFixture, "Stored account fields must remain flat and camelCase.");
        Check(!user.ToString().Contains(hashFixture, StringComparison.Ordinal) && !user.ToString().Contains(saltFixture, StringComparison.Ordinal), "Account ToString must not expose hash material.");
    }
}

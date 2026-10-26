using System.IO;
using System.Text;
using ProgramMigrationAnalyzer.App.Authentication;

namespace ProgramMigrationAnalyzer.WpfChecks;

internal static class EmbeddedLoginChecks
{
    internal const string Username = "test-operator";
    internal const string Password = " Test-only-P@ss42 ";
    // Independently generated with Python hashlib.pbkdf2_hmac, not the verifier under test.
    internal const string Configuration = """
        {"schemaVersion":1,"username":"test-operator","algorithm":"PBKDF2-HMAC-SHA256","iterations":600000,
         "salt":"AAECAwQFBgcICQoLDA0ODw==","hash":"AaPgv8f27yyWCdnYKfHJ4yqyohfwW7H/hegDXNMTJAU="}
        """;

    internal static EmbeddedLoginVerifier CreateVerifier(string configuration = Configuration)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(configuration));
        return EmbeddedLoginVerifier.FromStream(stream);
    }

    internal static void Run()
    {
        var verifier = CreateVerifier();
        var replacementCharacterVerifier = CreateVerifier(Configuration.Replace(
            "AaPgv8f27yyWCdnYKfHJ4yqyohfwW7H/hegDXNMTJAU=", "FgALzwagzrU5wg6i7OfBDZBJLrKWbQc8MdTvVKuuZFs="));
        Check(replacementCharacterVerifier.Verify(Username, "\uFFFD"), "The independently derived Unicode password must authenticate.");
        Check(!replacementCharacterVerifier.Verify(Username, "\uD800") && !replacementCharacterVerifier.Verify(Username, "\uDC00"),
            "Invalid UTF-16 must not be silently replaced and accepted as a different password.");
        Check(verifier.Verify(Username, Password), "The independent PBKDF2 credential must accept the exact synthetic password.");
        foreach (var (username, password) in new[]
        {
            (Username, "wrong-password"), ("unknown", Password), ("TEST-OPERATOR", Password),
            (" " + Username, Password), (Username, Password.Trim()), (Username, Password.ToLowerInvariant()),
            ("", Password), (Username, ""), (Username, new string('x', 129)), (new string('x', 129), Password)
        }) Check(!verifier.Verify(username, password), "Incorrect, normalized or out-of-bounds credentials were accepted.");
        Check(!verifier.Verify(null!, null!), "Null credentials must fail closed.");
        foreach (var invalid in new[]
        {
            "{}", "not-json", Configuration.Replace("600000", "1"), Configuration.Replace("600000", "2000000000"),
            Configuration.Replace("PBKDF2-HMAC-SHA256", "SHA256"), Configuration.Replace("AAECAwQFBgcICQoLDA0ODw==", "AA=="),
            Configuration.Replace("AaPgv8f27yyWCdnYKfHJ4yqyohfwW7H/hegDXNMTJAU=", "invalid"),
            Configuration.Replace("test-operator", " "), Configuration.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            Configuration.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            Configuration.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"unknown\":true"),
            Configuration + new string(' ', 4096)
        })
        {
            try { CreateVerifier(invalid); throw new InvalidOperationException("Malformed embedded credentials must be rejected."); }
            catch (InvalidDataException) { }
        }
        // The default resource must exist, but its real verifier data is never printed or copied into tests.
        Check(!EmbeddedLoginVerifier.FromEmbeddedResource().Verify(Username, Password),
            "The production resource must not contain the synthetic test account.");
        Console.WriteLine("PASS embedded login: independent PBKDF2 vector, exact password handling, unknown account, bounds, malformed resource and production/test separation.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

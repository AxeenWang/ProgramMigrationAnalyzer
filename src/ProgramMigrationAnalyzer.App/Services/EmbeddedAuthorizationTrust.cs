using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using System.IO;
using System.Text;
namespace ProgramMigrationAnalyzer.App.Services;
internal static class EmbeddedAuthorizationTrust
{
    internal static AuthorizationTrust Load()
    {
        try
        {
            using var stream = typeof(EmbeddedAuthorizationTrust).Assembly.GetManifestResourceStream("ProgramMigrationAnalyzer.AuthorizationPublicKey");
            if (stream is null || stream.Length > 8192) return AuthorizationTrust.None;
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
            return AuthorizationTrust.FromPublicKeyPem(reader.ReadToEnd());
        }
        catch (Exception error) when (error is IOException or DecoderFallbackException or AuthorizationException)
        { return AuthorizationTrust.None; }
    }
}

using System.Security;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.LicenseIssuer.Services;

public sealed class IssuerAccountEditor
{
    public LocalAccountRecord CreateAccount(string username, string displayName, SecureString password)
    {
        var normalized = LocalAccountValidation.NormalizeUsername(username);
        var hash = new LocalPasswordHasher().Create(password);
        var user = new LocalAccountRecord(Guid.NewGuid(), normalized, displayName, true, hash.Algorithm,
            hash.Iterations, Convert.ToBase64String(hash.Salt), Convert.ToBase64String(hash.Hash));
        Validate([user]);
        return user;
    }
    public AuthorizationPayload Create(AuthorizationPayload current, string username, string displayName, SecureString password)
    {
        var normalized = LocalAccountValidation.NormalizeUsername(username);
        if (current.Users.Any(user => user.Username == normalized)) throw new ArgumentException("Account already exists.");
        return WithUsers(current, current.Users.Append(CreateAccount(normalized, displayName, password)).ToArray());
    }
    public AuthorizationPayload Reset(AuthorizationPayload current, string username, string displayName, SecureString password)
    {
        var normalized = LocalAccountValidation.NormalizeUsername(username);
        var existing = Find(current, normalized);
        var replacement = CreateAccount(normalized, displayName, password) with { UserId = existing.UserId, IsEnabled = existing.IsEnabled };
        return WithUsers(current, current.Users.Select(user => user.UserId == existing.UserId ? replacement : user).ToArray());
    }
    public AuthorizationPayload SetEnabled(AuthorizationPayload current, string username, bool enabled)
    {
        var existing = Find(current, LocalAccountValidation.NormalizeUsername(username));
        return WithUsers(current, current.Users.Select(user => user.UserId == existing.UserId ? user with { IsEnabled = enabled } : user).ToArray());
    }
    private static LocalAccountRecord Find(AuthorizationPayload current, string username) =>
        current.Users.SingleOrDefault(user => user.Username == username) ?? throw new ArgumentException("Account does not exist.");
    private static AuthorizationPayload WithUsers(AuthorizationPayload current, LocalAccountRecord[] users)
    {
        var result = current with { Users = Array.AsReadOnly(users) };
        ValidatePayload(result);
        return result;
    }
    internal static void ValidatePayload(AuthorizationPayload payload)
    {
        try { _ = AuthorizationPackageCodec.EncodePayload(payload); }
        catch (AuthorizationException) { throw new ArgumentException("Invalid authorization or account data."); }
    }
    private static void Validate(IReadOnlyList<LocalAccountRecord> users) => ValidatePayload(
        new(2, "ProgramMigrationAnalyzer", Guid.NewGuid(), 1, DateTimeOffset.UtcNow, users));
}

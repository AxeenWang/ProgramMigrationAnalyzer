using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.LicenseIssuer.Services;

public sealed class LicenseIssuanceService
{
    public AuthorizationPayload CreateNew(IReadOnlyList<LocalAccountRecord> users, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(users);
        var payload = new AuthorizationPayload(2, "ProgramMigrationAnalyzer", Guid.NewGuid(), 1,
            issuedAtUtc.ToUniversalTime(), Array.AsReadOnly(users.ToArray()));
        IssuerAccountEditor.ValidatePayload(payload);
        return payload;
    }
    public AuthorizationPayload Reissue(AuthorizationPayload edited, DateTimeOffset issuedAtUtc)
    {
        var payload = edited with { Revision = checked(edited.Revision + 1), IssuedAtUtc = issuedAtUtc.ToUniversalTime(),
            Users = Array.AsReadOnly(edited.Users.ToArray()) };
        IssuerAccountEditor.ValidatePayload(payload);
        return payload;
    }
}

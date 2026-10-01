namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public sealed record LocalAccountFile(int SchemaVersion, IReadOnlyList<LocalAccountRecord> Users);

public sealed record LocalAccountRecord(
    Guid UserId, string Username, string DisplayName, bool IsEnabled,
    string PasswordAlgorithm, int Iterations, string Salt, string PasswordHash)
{
    public override string ToString() => nameof(LocalAccountRecord);
}

public sealed record PasswordHashRecord(string Algorithm, int Iterations, byte[] Salt, byte[] Hash)
{
    public override string ToString() => nameof(PasswordHashRecord);
}

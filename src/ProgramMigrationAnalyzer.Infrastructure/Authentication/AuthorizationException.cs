namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public enum AuthorizationFailure
{
    Missing, InvalidData, InvalidSignature, UntrustedKey, UnsafeAccess, StorageUnavailable,
    Busy, StaleRevision, ReplacementConfirmationRequired
}

public sealed class AuthorizationException(AuthorizationFailure failure)
    : Exception($"Offline authorization: {failure}.")
{
    public AuthorizationFailure Failure { get; } = failure;
}

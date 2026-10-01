using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.Core.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class ContractChecks
{
    public static void Run() => CheckSupport.Run(
        (nameof(SessionStartsSignedOut), SessionStartsSignedOut),
        (nameof(SessionGenerationAndClear), SessionGenerationAndClear),
        (nameof(ResultCannotSucceedWithoutUser), ResultCannotSucceedWithoutUser),
        (nameof(CredentialHasRedactedToStringAndOwnedCopy), CredentialHasRedactedToStringAndOwnedCopy),
        (nameof(InvalidRequestsAreRejected), InvalidRequestsAreRejected));

    private static AuthenticatedUser User() => new(Guid.NewGuid(), "test.user", "Test user", "Local");

    private static void SessionStartsSignedOut()
    {
        IUserSession session = new AuthenticatedSession();
        Check(!session.IsAuthenticated && session.CurrentUser is null, "A new session must deny authenticated access.");
    }

    private static void SessionGenerationAndClear()
    {
        var session = new AuthenticatedSession();
        var user = User();
        var changes = 0;
        session.Changed += (_, _) => changes++;
        var firstGeneration = session.Generation;
        session.SetAuthenticated(user);
        Check(session.IsAuthenticated && session.CurrentUser == user, "Verified identity was not installed.");
        var authenticatedGeneration = session.Generation;
        session.Clear();
        Check(!session.IsAuthenticated && session.CurrentUser is null, "Clearing must remove the identity.");
        Check(authenticatedGeneration > firstGeneration && session.Generation > authenticatedGeneration && changes == 2,
            "Session transitions must invalidate pending work and notify commands.");
        Throws<ArgumentNullException>(() => session.SetAuthenticated(null!));
        Check(!session.IsAuthenticated, "Invalid identity must not authenticate the session.");
    }

    private static void ResultCannotSucceedWithoutUser()
    {
        Throws<ArgumentNullException>(() => AuthenticationResult.Succeeded(null!));
        var user = User();
        var success = AuthenticationResult.Succeeded(user);
        var failure = AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials);
        Check(success.IsSuccess && success.User == user && success.Failure is null, "Success must carry only a verified identity.");
        Check(!failure.IsSuccess && failure.User is null && failure.Failure == AuthenticationFailure.InvalidCredentials,
            "Failure must not carry an authenticated identity.");
        Throws<ArgumentOutOfRangeException>(() => AuthenticationResult.Failed((AuthenticationFailure)999));
    }

    private static void CredentialHasRedactedToStringAndOwnedCopy()
    {
        const string fixture = "Test-only credential 123";
        var original = Password(fixture);
        using var request = new LoginRequest(LoginRequestKind.Credentials, "test.user", original);
        original.Dispose();
        var owned = request.Password;
        Check(owned is not null && owned.Length == fixture.Length && owned.IsReadOnly(), "Request must own a read-only credential copy.");
        Check(!(request.ToString() ?? string.Empty).Contains(fixture, StringComparison.Ordinal), "Credential must not appear in ToString.");
        request.Dispose();
        Throws<ObjectDisposedException>(() => _ = request.Password);
        Throws<ObjectDisposedException>(() => _ = owned!.Length);
        request.Dispose();
    }

    private static void InvalidRequestsAreRejected()
    {
        using var password = Password("Test-only credential 123");
        using var empty = Password(string.Empty);
        Throws<ArgumentOutOfRangeException>(() => new LoginRequest((LoginRequestKind)999, "test.user", password));
        Throws<ArgumentException>(() => new LoginRequest(LoginRequestKind.Credentials, " ", password));
        Throws<ArgumentNullException>(() => new LoginRequest(LoginRequestKind.Credentials, "test.user", null));
        Throws<ArgumentException>(() => new LoginRequest(LoginRequestKind.Credentials, "test.user", empty));
        Throws<ArgumentException>(() => new LoginRequest(LoginRequestKind.Interactive, null, password));
        using var interactive = new LoginRequest(LoginRequestKind.Interactive, null, null);
        Check(interactive.Password is null, "Interactive requests cannot collect a password.");
    }
}

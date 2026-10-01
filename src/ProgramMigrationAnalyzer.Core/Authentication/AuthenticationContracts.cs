namespace ProgramMigrationAnalyzer.Core.Authentication;

public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default);
}

public interface IUserSession
{
    bool IsAuthenticated { get; }
    AuthenticatedUser? CurrentUser { get; }
    long Generation { get; }
    event EventHandler? Changed;
}

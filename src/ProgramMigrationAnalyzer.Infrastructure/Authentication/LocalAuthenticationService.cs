using ProgramMigrationAnalyzer.Core.Authentication;
using System.Security;
using System.Security.Cryptography;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public sealed class LocalAuthenticationService(LocalAccountStore store, LocalPasswordHasher hasher) : IAuthenticationService
{
    public async Task<AuthenticationResult> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Kind != LoginRequestKind.Credentials)
            return AuthenticationResult.Failed(AuthenticationFailure.UnsupportedRequest);
        try
        {
            var accounts = await store.ReadAsync(cancellationToken);
            string? username = null;
            try { username = LocalAccountValidation.NormalizeUsername(request.Username!); }
            catch (ArgumentException) { }
            var account = accounts.Users.SingleOrDefault(user => user.Username == username && user.IsEnabled);
            // Snapshot ownership allows the request and UI to be disposed during background work.
            using var password = request.Password!.Copy();
            var hash = account is null
                ? new PasswordHashRecord(LocalPasswordHasher.Algorithm, LocalPasswordHasher.CreationIterations,
                    RandomNumberGenerator.GetBytes(16), RandomNumberGenerator.GetBytes(32))
                : new PasswordHashRecord(account.PasswordAlgorithm, account.Iterations,
                    Convert.FromBase64String(account.Salt), Convert.FromBase64String(account.PasswordHash));
            var matches = await Task.Run(() => hasher.Verify(password, hash), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!matches || account is null)
                return AuthenticationResult.Failed(AuthenticationFailure.InvalidCredentials);
            return AuthenticationResult.Succeeded(new(account.UserId, account.Username, account.DisplayName, "Local"));
        }
        catch (LocalAccountConfigurationException) { return AuthenticationResult.Failed(AuthenticationFailure.ConfigurationInvalid); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or ObjectDisposedException or CryptographicException)
        { return AuthenticationResult.Failed(AuthenticationFailure.UnexpectedFailure); }
    }
}

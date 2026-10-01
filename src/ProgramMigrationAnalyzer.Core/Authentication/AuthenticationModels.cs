using System.Security;

namespace ProgramMigrationAnalyzer.Core.Authentication;

public enum LoginRequestKind
{
    Credentials,
    Interactive
}

public enum AuthenticationFailure
{
    InvalidCredentials,
    ConfigurationInvalid,
    UnsupportedRequest,
    UnexpectedFailure
}

public sealed record AuthenticatedUser(Guid UserId, string Username, string DisplayName, string Provider);

public sealed class AuthenticationResult
{
    private AuthenticationResult(AuthenticatedUser? user, AuthenticationFailure? failure)
    {
        User = user;
        Failure = failure;
    }

    public AuthenticatedUser? User { get; }
    public AuthenticationFailure? Failure { get; }
    public bool IsSuccess => User is not null;

    public static AuthenticationResult Succeeded(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new AuthenticationResult(user, null);
    }

    public static AuthenticationResult Failed(AuthenticationFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new AuthenticationResult(null, failure);
    }
}

public sealed class LoginRequest : IDisposable
{
    private SecureString? _password;
    private bool _disposed;

    public LoginRequest(LoginRequestKind kind, string? username, SecureString? password)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == LoginRequestKind.Credentials)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("A username is required.", nameof(username));
            }

            ArgumentNullException.ThrowIfNull(password);
            if (password.Length == 0)
            {
                throw new ArgumentException("A password is required.", nameof(password));
            }

            _password = password.Copy();
            _password.MakeReadOnly();
        }
        else if (password is not null)
        {
            throw new ArgumentException("Interactive authentication cannot accept a password.", nameof(password));
        }

        Kind = kind;
        Username = username;
    }

    public LoginRequestKind Kind { get; }
    public string? Username { get; }

    public SecureString? Password
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _password;
        }
    }

    public override string ToString() => nameof(LoginRequest);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _password?.Dispose();
        _password = null;
    }
}

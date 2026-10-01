using ProgramMigrationAnalyzer.Core.Authentication;

namespace ProgramMigrationAnalyzer.App.Services;

internal sealed class AuthenticatedSession : IUserSession
{
    public bool IsAuthenticated => CurrentUser is not null;
    public AuthenticatedUser? CurrentUser { get; private set; }
    public long Generation { get; private set; }
    public event EventHandler? Changed;

    internal void SetAuthenticated(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        Generation = checked(Generation + 1);
        CurrentUser = user;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Clear()
    {
        Generation = checked(Generation + 1);
        CurrentUser = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

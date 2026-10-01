using System.Security;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public sealed class LocalAccountAdministrationService(LocalAccountStore store, LocalPasswordHasher hasher, ILocalAccountAccessPolicy policy)
{
    public bool IsElevatedAdministrator => policy.IsElevatedAdministrator;

    public Task CreateOrResetAsync(string username, string displayName, SecureString password, CancellationToken cancellationToken = default) =>
        SavePasswordAsync(username, displayName, password, mustExist: null, cancellationToken);

    // The UI exposes separate modes so a typo cannot create or reset the wrong account.
    public Task CreateAsync(string username, string displayName, SecureString password, CancellationToken cancellationToken = default) =>
        SavePasswordAsync(username, displayName, password, mustExist: false, cancellationToken);
    public Task ResetAsync(string username, string displayName, SecureString password, CancellationToken cancellationToken = default) =>
        SavePasswordAsync(username, displayName, password, mustExist: true, cancellationToken);

    private async Task SavePasswordAsync(string username, string displayName, SecureString password, bool? mustExist,
        CancellationToken cancellationToken)
    {
        RequireAdministrator();
        cancellationToken.ThrowIfCancellationRequested();
        username = LocalAccountValidation.NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A display name is required.", nameof(displayName));
        using var ownedPassword = password.Copy();
        var hash = await Task.Run(() => hasher.Create(ownedPassword), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await store.UpdateAsync(file => {
            var existing = file.Users.SingleOrDefault(user => user.Username == username);
            if (mustExist == true && existing is null)
                throw new ArgumentException("The account does not exist.");
            if (mustExist == false && existing is not null)
                throw new ArgumentException("The account already exists. Choose reset to change its password.");
            var replacement = new LocalAccountRecord(existing?.UserId ?? Guid.NewGuid(), username, displayName,
                existing?.IsEnabled ?? true, hash.Algorithm, hash.Iterations,
                Convert.ToBase64String(hash.Salt), Convert.ToBase64String(hash.Hash));
            return new(1, file.Users.Where(user => user.Username != username).Append(replacement).ToArray());
        }, cancellationToken);
    }

    public Task SetEnabledAsync(string username, bool isEnabled, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        cancellationToken.ThrowIfCancellationRequested();
        username = LocalAccountValidation.NormalizeUsername(username);
        return store.UpdateAsync(file => {
            if (!file.Users.Any(user => user.Username == username))
                throw new ArgumentException("The account does not exist.");
            return new(1, file.Users.Select(user => user.Username == username ? user with { IsEnabled = isEnabled } : user).ToArray());
        }, cancellationToken);
    }

    private void RequireAdministrator()
    {
        if (!policy.IsElevatedAdministrator)
            throw new UnauthorizedAccessException("Local account management requires an elevated Windows administrator.");
    }
}

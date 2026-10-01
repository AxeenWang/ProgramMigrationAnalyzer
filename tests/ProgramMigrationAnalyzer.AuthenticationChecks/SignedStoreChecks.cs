using System.IO;
using System.Text;
using ProgramMigrationAnalyzer.Core.Authentication;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class SignedStoreChecks
{
    public static void Run() => CheckSupport.Run(
        (nameof(ValidEnabledCredentialsRequireSignature), () => ValidEnabledCredentialsRequireSignature().GetAwaiter().GetResult()),
        (nameof(UnsignedAndTamperedStoreFailsClosed), () => UnsignedAndTamperedStoreFailsClosed().GetAwaiter().GetResult()),
        (nameof(ReauthenticationReadsAgain), () => ReauthenticationReadsAgain().GetAwaiter().GetResult()),
        (nameof(TwoDeploymentsUseSameKey), () => TwoDeploymentsUseSameKey().GetAwaiter().GetResult()),
        (nameof(RevisionAndIdRules), () => RevisionAndIdRules().GetAwaiter().GetResult()),
        (nameof(ReplacementConfirmationUsesLockedCurrentId), () => ReplacementConfirmationUsesLockedCurrentId().GetAwaiter().GetResult()),
        (nameof(InterruptedImportRollsBack), () => InterruptedImportRollsBack().GetAwaiter().GetResult()),
        (nameof(CanceledAndNonElevatedImportPreservesFile), () => CanceledAndNonElevatedImportPreservesFile().GetAwaiter().GetResult()),
        (nameof(UnsafePathsLockAndBusy), () => UnsafePathsLockAndBusy().GetAwaiter().GetResult()),
        (nameof(MissingPolicyCanInitializeAndAuthenticate), () => MissingPolicyCanInitializeAndAuthenticate().GetAwaiter().GetResult()));

    private static async Task MissingPolicyCanInitializeAndAuthenticate()
    {
        using var f = new SignedAccountFixture();
        var store = new SignedLocalAccountStore(f.FilePath, new MissingAwarePolicy(f.Policy), f.Key.Verifier);
        var missing = await StoreChecks.Expect<AuthorizationException>(() => store.ReadAsync());
        CheckSupport.Check(missing.Failure == AuthorizationFailure.Missing, "Missing account path classification changed.");
        await store.ImportAsync(f.Issue());
        using var secret = CheckSupport.Password("Test@!#1");
        using var request = new LoginRequest(LoginRequestKind.Credentials, "internal.one", secret);
        CheckSupport.Check((await new SignedLocalAuthenticationService(store, new()).AuthenticateAsync(request)).IsSuccess,
            "First import cannot initialize a path reported missing by the ACL policy.");
    }

    private static async Task ValidEnabledCredentialsRequireSignature()
    {
        using var f = new SignedAccountFixture(); await f.Store.ImportAsync(f.Issue());
        CheckSupport.Check((await f.Login()).IsSuccess, "Signed enabled user cannot authenticate.");
        CheckSupport.Check(!(await f.Login(password: "wrong@!#")).IsSuccess && !(await f.Login(username: "unknown")).IsSuccess, "Invalid credentials admitted.");
        await f.Store.ImportAsync(f.Issue(2, enabled: false));
        CheckSupport.Check(!(await f.Login()).IsSuccess, "Disabled signed user admitted.");
    }
    private static async Task UnsignedAndTamperedStoreFailsClosed()
    {
        using var f = new SignedAccountFixture(); Directory.CreateDirectory(f.DirectoryPath);
        await File.WriteAllTextAsync(f.FilePath, "{\"schemaVersion\":1,\"users\":[]}");
        CheckSupport.Check((await f.Login()).Failure == AuthenticationFailure.ConfigurationInvalid, "Unsigned data admitted.");
        await f.Store.ImportAsync(f.Issue());
        var bytes = await File.ReadAllBytesAsync(f.FilePath); bytes[bytes.Length / 2] ^= 1; await File.WriteAllBytesAsync(f.FilePath, bytes);
        CheckSupport.Check((await f.Login()).Failure == AuthenticationFailure.ConfigurationInvalid, "Tampered data admitted.");
    }
    private static async Task ReauthenticationReadsAgain()
    {
        using var f = new SignedAccountFixture(); await f.Store.ImportAsync(f.Issue());
        CheckSupport.Check((await f.Login()).IsSuccess, "Initial authentication failed.");
        await f.Store.ImportAsync(f.Issue(2, password: "New@!#12"));
        CheckSupport.Check(!(await f.Login()).IsSuccess && (await f.Login(password: "New@!#12")).IsSuccess, "Reauthentication used stale account data.");
    }
    private static async Task TwoDeploymentsUseSameKey()
    {
        using var key = new AuthorizationTestFixture(); using var a = new SignedAccountFixture(key); using var b = new SignedAccountFixture(key);
        var envelope = a.Issue(); await a.Store.ImportAsync(envelope); await b.Store.ImportAsync(envelope);
        CheckSupport.Check((await a.Login()).IsSuccess && (await b.Login()).IsSuccess, "Key is incorrectly machine bound.");
    }
    private static async Task RevisionAndIdRules()
    {
        using var f = new SignedAccountFixture(); var first = f.Issue(); await f.Store.ImportAsync(first);
        CheckSupport.Check(!(await f.Store.ImportAsync(first)).Changed, "Identical reimport should be idempotent.");
        var sameRevision = await StoreChecks.Expect<AuthorizationException>(() => f.Store.ImportAsync(f.Issue()));
        CheckSupport.Check(sameRevision.Failure == AuthorizationFailure.StaleRevision, "Different payload at same revision admitted.");
        await f.Store.ImportAsync(f.Issue(2));
        var older = await StoreChecks.Expect<AuthorizationException>(() => f.Store.ImportAsync(first));
        CheckSupport.Check(older.Failure == AuthorizationFailure.StaleRevision, "Older revision admitted.");
        var currentId = (await f.Store.ReadAsync()).Payload.AuthorizationId; var newId = Guid.NewGuid(); var different = f.Issue(id: newId);
        var confirmation = await StoreChecks.Expect<AuthorizationException>(() => f.Store.ImportAsync(different));
        CheckSupport.Check(confirmation.Failure == AuthorizationFailure.ReplacementConfirmationRequired, "Cross-authorization import skipped confirmation.");
        await f.Store.ImportAsync(different, currentId);
        CheckSupport.Check((await f.Store.ReadAsync()).Payload.AuthorizationId == newId, "Confirmed replacement failed.");
    }
    private static async Task ReplacementConfirmationUsesLockedCurrentId()
    {
        using var f = new SignedAccountFixture(); await f.Store.ImportAsync(f.Issue()); var oldId = (await f.Store.ReadAsync()).Payload.AuthorizationId;
        var held = new FileStream(f.FilePath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var pending = f.Store.ImportAsync(f.Issue(id: Guid.NewGuid()), oldId);
            await Task.Delay(100);
            CheckSupport.Check(!pending.IsCompleted, "Writer did not wait for target lock.");
            var changedId = Guid.NewGuid(); var changed = f.Issue(id: changedId); await File.WriteAllBytesAsync(f.FilePath, changed);
            held.Dispose();
            var error = await StoreChecks.Expect<AuthorizationException>(() => pending);
            CheckSupport.Check(error.Failure == AuthorizationFailure.ReplacementConfirmationRequired
                && (await f.Store.ReadAsync()).Payload.AuthorizationId == changedId, "Writer trusted stale replacement confirmation.");
        }
        finally { held.Dispose(); }
    }
    private static async Task InterruptedImportRollsBack()
    {
        using var f = new SignedAccountFixture(); var original = f.Issue(); await f.Store.ImportAsync(original); f.Policy.ResetTemporaryState();
        f.Policy.FailSecuringTemporary = true;
        await StoreChecks.Expect<AuthorizationException>(() => f.Store.ImportAsync(f.Issue(2)));
        CheckSupport.Check((await File.ReadAllBytesAsync(f.FilePath)).SequenceEqual(original), "Interrupted import corrupted prior data.");
        f.Policy.FailSecuringTemporary = false; f.Policy.RejectFinalValidation = true; f.Policy.ProbeRollbackLock = true;
        await StoreChecks.Expect<AuthorizationException>(() => f.Store.ImportAsync(f.Issue(2)));
        CheckSupport.Check((await File.ReadAllBytesAsync(f.FilePath)).SequenceEqual(original) && f.Policy.RollbackWasLocked,
            "Rollback did not restore original data under writer lock.");
    }
    private static async Task CanceledAndNonElevatedImportPreservesFile()
    {
        using var f = new SignedAccountFixture(); var first = f.Issue(); await f.Store.ImportAsync(first);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await StoreChecks.Expect<OperationCanceledException>(() => f.Store.ImportAsync(f.Issue(2), ct: cancellation.Token));
        f.Policy.IsElevatedAdministrator = false;
        await StoreChecks.Expect<AuthorizationException>(() => f.Store.ImportAsync(f.Issue(2)));
        CheckSupport.Check((await File.ReadAllBytesAsync(f.FilePath)).SequenceEqual(first), "Canceled/non-elevated import changed account data.");
    }
    private static async Task UnsafePathsLockAndBusy()
    {
        using var f = new SignedAccountFixture(); await f.Store.ImportAsync(f.Issue()); f.Policy.ResetTemporaryState();
        f.Policy.RejectLockPath = true;
        var unsafePath = await StoreChecks.Expect<AuthorizationException>(() => f.Store.ImportAsync(f.Issue(2)));
        CheckSupport.Check(unsafePath.Failure == AuthorizationFailure.UnsafeAccess, "Unsafe lock path admitted.");
        f.Policy.RejectLockPath = false;
        using var held = new FileStream(f.FilePath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await StoreChecks.Expect<OperationCanceledException>(() => f.Store.ImportAsync(f.Issue(2), ct: cancel.Token));
        var busy = await StoreChecks.Expect<AuthorizationException>(() => f.Store.ImportAsync(f.Issue(2)));
        CheckSupport.Check(busy.Failure == AuthorizationFailure.Busy, "Locked store did not report bounded busy failure.");
    }
}

internal sealed class MissingAwarePolicy(FakeAccountPolicy inner) : ILocalAccountAccessPolicy
{
    public bool IsElevatedAdministrator => inner.IsElevatedAdministrator;
    public void PrepareWriteAccess(string path) => inner.PrepareWriteAccess(path);
    public void SecureFile(string path) => inner.SecureFile(path);
    public void ValidateReadAccess(string path)
    {
        inner.ValidateReadAccess(path);
        if (!File.Exists(path)) throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.Missing);
    }
}

internal sealed class SignedAccountFixture : IDisposable
{
    private readonly bool _ownsKey;
    internal AuthorizationTestFixture Key { get; }
    internal string DirectoryPath { get; }
    internal string FilePath => Path.Combine(DirectoryPath, "users.json");
    internal FakeAccountPolicy Policy { get; } = new();
    internal SignedLocalAccountStore Store { get; }
    internal SignedLocalAuthenticationService Authentication { get; }
    internal SignedAccountFixture(AuthorizationTestFixture? key = null)
    {
        _ownsKey = key is null; Key = key ?? new();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ProgramMigrationAnalyzer.sln"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Test root unavailable.");
        DirectoryPath = Path.Combine(root.FullName, ".codex-tmp", "2026-10-01_offline-authorization-phase-5", "signed-store", Guid.NewGuid().ToString("N"));
        Store = new(FilePath, Policy, Key.Verifier); Authentication = new(Store, new());
    }
    internal byte[] Issue(int revision = 1, bool enabled = true, string password = "Test@!#1", Guid? id = null)
    {
        var payload = Key.Payload(revision); var user = payload["users"]![0]!;
        using var secret = CheckSupport.Password(password); var hash = new LocalPasswordHasher().Create(secret);
        user["salt"] = Convert.ToBase64String(hash.Salt); user["passwordHash"] = Convert.ToBase64String(hash.Hash); user["isEnabled"] = enabled;
        if (id is not null) payload["authorizationId"] = id.Value.ToString();
        return Key.Issue(payload);
    }
    internal async Task<AuthenticationResult> Login(string username = "internal.one", string password = "Test@!#1")
    {
        using var secret = CheckSupport.Password(password); using var request = new LoginRequest(LoginRequestKind.Credentials, username, secret);
        return await Authentication.AuthenticateAsync(request);
    }
    public void Dispose()
    {
        if (_ownsKey) Key.Dispose();
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
    }
}

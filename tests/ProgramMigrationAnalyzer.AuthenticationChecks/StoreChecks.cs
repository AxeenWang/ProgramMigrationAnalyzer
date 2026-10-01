using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProgramMigrationAnalyzer.Core.Authentication;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class StoreChecks
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static LocalAccountRecord Account(string username = "user.one", bool enabled = true)
    {
        using var password = Password("  Test-only 密碼😀 credential  ");
        var hash = new LocalPasswordHasher().Create(password);
        return new(Guid.NewGuid(), username, "Test user", enabled, hash.Algorithm, hash.Iterations,
            Convert.ToBase64String(hash.Salt), Convert.ToBase64String(hash.Hash));
    }

    public static void Run() => CheckSupport.Run(
        (nameof(ValidEnabledAccountOnly), () => ValidEnabledAccountOnly().GetAwaiter().GetResult()),
        (nameof(InvalidAccountFileFailsClosed), () => InvalidAccountFileFailsClosed().GetAwaiter().GetResult()),
        (nameof(AclAndInterruptedWrite), () => AclAndInterruptedWrite().GetAwaiter().GetResult()),
        (nameof(CanceledUpdatePreservesOldFile), () => CanceledUpdatePreservesOldFile().GetAwaiter().GetResult()),
        (nameof(ConcurrentUpdatesReadLatest), () => ConcurrentUpdatesReadLatest().GetAwaiter().GetResult()),
        (nameof(LockTimeoutAndCancellation), () => LockTimeoutAndCancellation().GetAwaiter().GetResult()),
        (nameof(InitializationAndCorruptUpdate), () => InitializationAndCorruptUpdate().GetAwaiter().GetResult()),
        (nameof(NonElevatedStoreCannotWrite), () => NonElevatedStoreCannotWrite().GetAwaiter().GetResult()),
        (nameof(UnsafeLockPathFailsBeforeOpening), () => UnsafeLockPathFailsBeforeOpening().GetAwaiter().GetResult()),
        (nameof(OversizedUpdatePreservesOldFile), () => OversizedUpdatePreservesOldFile().GetAwaiter().GetResult()),
        (nameof(PostReplacementValidationRollsBack), () => PostReplacementValidationRollsBack().GetAwaiter().GetResult()),
        (nameof(WindowsAclChecks.VolumeRootDeleteCannotReplaceDeployment), WindowsAclChecks.VolumeRootDeleteCannotReplaceDeployment),
        (nameof(WindowsAclChecks.ReplacementAndRootControlRemainForbidden), WindowsAclChecks.ReplacementAndRootControlRemainForbidden));

    private static async Task ValidEnabledAccountOnly()
    {
        using var fixture = new AccountFixture();
        var account = Account();
        await fixture.Write(new(1, [account]));
        var original = await File.ReadAllBytesAsync(fixture.FilePath);
        var modified = File.GetLastWriteTimeUtc(fixture.FilePath);
        var result = await fixture.Authenticate(" User.One ", "  Test-only 密碼😀 credential  ");
        Check(result.User is { Provider: "Local", Username: "user.one", DisplayName: "Test user" }
            && result.User.UserId == account.UserId, "Enabled account with exact password must authenticate.");
        foreach (var (username, password) in new[] { ("unknown", "  Test-only 密碼😀 credential  "),
            ("user.one", "incorrect test password"), ("user/name", "incorrect test password") })
            Check((await fixture.Authenticate(username, password)).Failure == AuthenticationFailure.InvalidCredentials,
                "Unknown, invalid or wrong credentials must have the same failure.");
        Check(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(fixture.FilePath))
            && modified == File.GetLastWriteTimeUtc(fixture.FilePath), "Authentication must never rewrite account data.");
        await fixture.Write(new(1, [account with { IsEnabled = false }]));
        Check((await fixture.Authenticate("user.one", "  Test-only 密碼😀 credential  ")).Failure
            == AuthenticationFailure.InvalidCredentials, "Each login must reread disabled state.");
        using var interactive = new LoginRequest(LoginRequestKind.Interactive, null, null);
        Check((await fixture.Authentication.AuthenticateAsync(interactive)).Failure == AuthenticationFailure.UnsupportedRequest,
            "Local provider must reject interactive requests.");
    }

    private static async Task InvalidAccountFileFailsClosed()
    {
        using var fixture = new AccountFixture();
        var account = Account();
        Check((await fixture.Authenticate("user.one", "test credential")).Failure == AuthenticationFailure.ConfigurationInvalid
            && !Directory.Exists(fixture.DirectoryPath), "Missing configuration must fail without creating directories.");
        var valid = JsonSerializer.Serialize(new LocalAccountFile(1, [account]), JsonOptions);
        var cases = new List<string> { "{", "null", "{}", valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            JsonSerializer.Serialize(new LocalAccountFile(1, [account, account with { Username = " User.One ", UserId = Guid.NewGuid() }]), JsonOptions),
            JsonSerializer.Serialize(new LocalAccountFile(1, [account, account with { Username = "user.two" }]), JsonOptions) };
        foreach (var (key, value) in new (string, JsonNode?)[] {
            ("salt", JsonValue.Create("not base64")), ("passwordHash", JsonValue.Create("AQ==")),
            ("iterations", JsonValue.Create(2_000_001)), ("iterations", JsonValue.Create(599_999)),
            ("isEnabled", JsonValue.Create("true")), ("isEnabled", null),
            ("userId", JsonValue.Create(Guid.Empty.ToString())), ("username", JsonValue.Create("bad/name")),
            ("displayName", JsonValue.Create("")), ("passwordAlgorithm", JsonValue.Create("SHA256")) })
        {
            var node = JsonNode.Parse(valid)!;
            node["users"]![0]![key] = value;
            cases.Add(node.ToJsonString());
        }
        var missing = JsonNode.Parse(valid)!;
        ((JsonObject)missing["users"]![0]!).Remove("isEnabled");
        cases.Add(missing.ToJsonString());
        cases.Add(valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"));
        foreach (var json in cases)
        {
            Directory.CreateDirectory(fixture.DirectoryPath);
            await File.WriteAllTextAsync(fixture.FilePath, json);
            Check((await fixture.Authenticate("user.one", "test credential")).Failure == AuthenticationFailure.ConfigurationInvalid,
                "Invalid account file must fail closed.");
            Check(await File.ReadAllTextAsync(fixture.FilePath) == json, "Invalid configuration must not be repaired by login.");
        }
    }

    private static async Task AclAndInterruptedWrite()
    {
        using var fixture = new AccountFixture();
        var account = Account();
        await fixture.Write(new(1, [account]));
        foreach (var unsafeAccess in new[] { "Write", "Delete", "ChangePermissions", "ParentReplacement", "ReparsePoint" })
        {
            fixture.Policy.ReadFailure = unsafeAccess;
            Check((await fixture.Authenticate("user.one", "test credential")).Failure == AuthenticationFailure.ConfigurationInvalid,
                "Unsafe file or parent access must prevent authentication.");
        }
        fixture.Policy.ReadFailure = null;
        var original = await File.ReadAllBytesAsync(fixture.FilePath);
        fixture.Policy.FailSecuringTemporary = true;
        await Expect<LocalAccountConfigurationException>(() => fixture.Store.UpdateAsync(file =>
            file with { Users = [account with { DisplayName = "Changed" }] }));
        Check(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(fixture.FilePath)), "Interrupted write must preserve the old file.");
        fixture.Policy.FailSecuringTemporary = false;
        await fixture.Store.UpdateAsync(file => file with { Users = [account with { DisplayName = "Changed" }] });
        Check((await fixture.Store.ReadAsync()).Users[0].DisplayName == "Changed", "Successful replacement must be readable.");
        Check(fixture.Policy.SecuredTemporary && fixture.Policy.ValidatedPaths.Contains(fixture.FilePath),
            "Replacement must secure the new file and validate the final file.");
        Check(!Directory.EnumerateFiles(fixture.DirectoryPath, "*.tmp").Any(), "Failed and successful updates must remove scratch files.");
    }

    private static async Task CanceledUpdatePreservesOldFile()
    {
        using var fixture = new AccountFixture();
        var account = Account();
        await fixture.Write(new(1, [account]));
        using var cancellation = new CancellationTokenSource();
        await Expect<OperationCanceledException>(() => fixture.Store.UpdateAsync(file => {
            cancellation.Cancel();
            return file with { Users = [account with { IsEnabled = false }] };
        }, cancellation.Token));
        Check((await fixture.Store.ReadAsync()).Users[0].IsEnabled, "Cancellation before commit must retain old data.");
        await fixture.Store.UpdateAsync(file => file);
    }

    private static async Task ConcurrentUpdatesReadLatest()
    {
        using var fixture = new AccountFixture();
        var account = Account();
        await fixture.Write(new(1, [account]));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var first = Task.Run(() => fixture.Store.UpdateAsync(file => {
            entered.SetResult();
            Check(release.Wait(TimeSpan.FromSeconds(3)), "Test writer must be released.");
            return file with { Users = [account with { DisplayName = "First" }] };
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var secondStore = new LocalAccountStore(fixture.FilePath, fixture.Policy);
        var second = secondStore.UpdateAsync(file => file with { Users = [file.Users[0] with { IsEnabled = false }] });
        release.Set();
        await Task.WhenAll(first, second);
        var saved = (await fixture.Store.ReadAsync()).Users[0];
        Check(saved.DisplayName == "First" && !saved.IsEnabled, "Lock holder must reread latest file and retain both updates.");
    }

    private static async Task LockTimeoutAndCancellation()
    {
        using var fixture = new AccountFixture();
        await fixture.Write(new(1, [Account()]));
        var before = await File.ReadAllBytesAsync(fixture.FilePath);
        using (var locked = new FileStream(fixture.FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Expect<OperationCanceledException>(() => fixture.Store.UpdateAsync(file => file, cancellation.Token));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var error = await Expect<LocalAccountConfigurationException>(() => fixture.Store.UpdateAsync(file => file));
            Check(error.Failure == LocalAccountConfigurationFailure.Busy && clock.Elapsed < TimeSpan.FromSeconds(6),
                "Contended lock must fail safely within five-second wait plus scheduling tolerance.");
        }
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(fixture.FilePath)), "Waiting writers must not alter the file.");
        await fixture.Store.UpdateAsync(file => file);
    }

    private static async Task InitializationAndCorruptUpdate()
    {
        using var fixture = new AccountFixture();
        await fixture.Store.UpdateAsync(file => {
            Check(file.SchemaVersion == 1 && file.Users.Count == 0, "Only management may initialize an empty schema.");
            return file with { Users = [Account()] };
        });
        Check((await fixture.Store.ReadAsync()).Users.Count == 1, "First account must be persisted.");
        await File.WriteAllTextAsync(fixture.FilePath, "{");
        var called = false;
        await Expect<LocalAccountConfigurationException>(() => fixture.Store.UpdateAsync(file => {
            called = true;
            return new(1, []);
        }));
        Check(!called && await File.ReadAllTextAsync(fixture.FilePath) == "{", "Corrupt existing data cannot be silently replaced.");
    }

    private static async Task NonElevatedStoreCannotWrite()
    {
        using var fixture = new AccountFixture();
        fixture.Policy.IsElevatedAdministrator = false;
        await Expect<UnauthorizedAccessException>(() => fixture.Store.UpdateAsync(file => new(1, [Account()])));
        Check(!Directory.Exists(fixture.DirectoryPath), "Non-elevated write must have no filesystem effects.");
    }

    private static async Task UnsafeLockPathFailsBeforeOpening()
    {
        using var fixture = new AccountFixture();
        var account = Account();
        await fixture.Write(new(1, [account]));
        await File.WriteAllTextAsync(fixture.FilePath + ".lock", "test lock");
        fixture.Policy.RejectLockPath = true;
        var before = await File.ReadAllBytesAsync(fixture.FilePath);
        await Expect<LocalAccountConfigurationException>(() => fixture.Store.UpdateAsync(file =>
            file with { Users = [account with { IsEnabled = false }] }));
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(fixture.FilePath)),
            "Unsafe/reparse lock path must prevent updates before it is opened.");
    }

    private static async Task OversizedUpdatePreservesOldFile()
    {
        using var fixture = new AccountFixture();
        var account = Account();
        await fixture.Write(new(1, [account]));
        var before = await File.ReadAllBytesAsync(fixture.FilePath);
        await Expect<LocalAccountConfigurationException>(() => fixture.Store.UpdateAsync(file =>
            file with { Users = [account with { DisplayName = new string('x', 4 * 1024 * 1024) }] }));
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(fixture.FilePath)),
            "Management must not commit a file that exceeds its own read limit.");
    }

    private static async Task PostReplacementValidationRollsBack()
    {
        using var fixture = new AccountFixture();
        var account = Account();
        await fixture.Write(new(1, [account]));
        var before = await File.ReadAllBytesAsync(fixture.FilePath);
        fixture.Policy.RejectFinalValidation = true;
        fixture.Policy.ProbeRollbackLock = true;
        await Expect<LocalAccountConfigurationException>(() => fixture.Store.UpdateAsync(file =>
            file with { Users = [account with { IsEnabled = false }] }));
        fixture.Policy.RejectFinalValidation = false;
        Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(fixture.FilePath)),
            "Failure after replacement must restore the exact old account file.");
        Check(fixture.Policy.RollbackWasLocked, "Backup recovery must retain the writer lock until restoration completes.");
        fixture.Policy.ResetTemporaryState();
        await fixture.Store.UpdateAsync(file => file);
        Check(!Directory.EnumerateFiles(fixture.DirectoryPath, "*.tmp").Any(), "Completed rollback and update must clean safe backups.");
    }

    internal static async Task<TException> Expect<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}

internal sealed class AccountFixture : IDisposable
{
    public AccountFixture(string taskDirectory = "2026-10-01_login-phase-2")
    {
        DirectoryPath = Path.Combine(FindRoot(), ".codex-tmp", taskDirectory, "accounts", Guid.NewGuid().ToString("N"), "auth");
        FilePath = Path.Combine(DirectoryPath, "users.json");
        Store = new(FilePath, Policy);
        Authentication = new(Store, new());
    }
    public string DirectoryPath { get; }
    public string FilePath { get; }
    public FakeAccountPolicy Policy { get; } = new();
    public LocalAccountStore Store { get; }
    public LocalAuthenticationService Authentication { get; }
    public async Task Write(LocalAccountFile file)
    {
        Directory.CreateDirectory(DirectoryPath);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(file, StoreChecks.JsonOptions));
    }
    public async Task<AuthenticationResult> Authenticate(string username, string value)
    {
        using var password = Password(value);
        using var request = new LoginRequest(LoginRequestKind.Credentials, username, password);
        return await Authentication.AuthenticateAsync(request);
    }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProgramMigrationAnalyzer.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Test root unavailable.");
    }
    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}

internal sealed class FakeAccountPolicy : ILocalAccountAccessPolicy
{
    public bool IsElevatedAdministrator { get; set; } = true;
    public string? ReadFailure { get; set; }
    public bool FailSecuringTemporary { get; set; }
    public bool RejectLockPath { get; set; }
    public bool RejectFinalValidation { get; set; }
    public bool ProbeRollbackLock { get; set; }
    public bool RollbackWasLocked { get; private set; }
    public bool SecuredTemporary { get; private set; }
    public HashSet<string> ValidatedPaths { get; } = [];
    public void ValidateReadAccess(string filePath)
    {
        ValidatedPaths.Add(filePath);
        if (ProbeRollbackLock && Path.GetFileName(filePath).StartsWith(".users-backup-", StringComparison.Ordinal))
        {
            try
            {
                using var competing = new FileStream(Path.Combine(Path.GetDirectoryName(filePath)!, "users.json.lock"),
                    FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33) { RollbackWasLocked = true; }
        }
        if (ReadFailure is not null || (RejectLockPath && filePath.EndsWith(".lock", StringComparison.Ordinal))
            || (RejectFinalValidation && SecuredTemporary && filePath.EndsWith("users.json", StringComparison.Ordinal)))
            throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.UnsafeAccess);
    }
    public void PrepareWriteAccess(string directoryPath) => Directory.CreateDirectory(directoryPath);
    public void SecureFile(string filePath)
    {
        if (Path.GetExtension(filePath) == ".tmp")
        {
            if (FailSecuringTemporary) throw new IOException("Simulated write interruption.");
            SecuredTemporary = true;
        }
    }
    public void ResetTemporaryState() => SecuredTemporary = false;
}

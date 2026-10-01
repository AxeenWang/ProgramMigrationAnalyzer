using System.Diagnostics;
using System.Text.Json;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public enum LocalAccountConfigurationFailure { Missing, InvalidData, UnsafeAccess, StorageUnavailable, Busy }

public sealed class LocalAccountConfigurationException(LocalAccountConfigurationFailure failure)
    : Exception($"Local account configuration: {failure}.")
{
    public LocalAccountConfigurationFailure Failure { get; } = failure;
}

public sealed class LocalAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const int MaximumFileBytes = 4 * 1024 * 1024;
    private readonly string _filePath;
    private readonly ILocalAccountAccessPolicy _accessPolicy;

    public LocalAccountStore(string filePath, ILocalAccountAccessPolicy accessPolicy)
    {
        _filePath = Path.GetFullPath(filePath);
        _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ProgramMigrationAnalyzer", "auth", "users.json");

    public async Task<LocalAccountFile> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _accessPolicy.ValidateReadAccess(_filePath);
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length > MaximumFileBytes) throw InvalidData();
            using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
            var root = document.RootElement;
            RequireProperties(root, "schemaVersion", "users");
            if (root.GetProperty("schemaVersion").GetInt32() != 1) throw InvalidData();
            var users = root.GetProperty("users");
            if (users.ValueKind != JsonValueKind.Array) throw InvalidData();
            var records = new List<LocalAccountRecord>();
            foreach (var user in users.EnumerateArray())
            {
                RequireProperties(user, "userId", "username", "displayName", "isEnabled",
                    "passwordAlgorithm", "iterations", "salt", "passwordHash");
                records.Add(new(user.GetProperty("userId").GetGuid(),
                    RequiredString(user, "username"), RequiredString(user, "displayName"),
                    user.GetProperty("isEnabled").GetBoolean(), RequiredString(user, "passwordAlgorithm"),
                    user.GetProperty("iterations").GetInt32(), RequiredString(user, "salt"),
                    RequiredString(user, "passwordHash")));
            }
            return Validate(new(1, records));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException or OverflowException)
        { throw InvalidData(); }
        catch (FileNotFoundException) { throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.Missing); }
        catch (DirectoryNotFoundException) { throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.Missing); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.StorageUnavailable); }
    }

    public async Task UpdateAsync(Func<LocalAccountFile, LocalAccountFile> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_accessPolicy.IsElevatedAdministrator)
            throw new UnauthorizedAccessException("Local account management requires an elevated Windows administrator.");

        var directory = Path.GetDirectoryName(_filePath)!;
        string? temporary = null;
        try
        {
            _accessPolicy.PrepareWriteAccess(directory);
            // The lock is persistent. Deleting a lock path on release can create two lock identities.
            await using var heldLock = await AcquireLockAsync(cancellationToken);
            _accessPolicy.SecureFile(_filePath + ".lock");
            LocalAccountFile current;
            try { current = await ReadAsync(cancellationToken); }
            catch (LocalAccountConfigurationException exception) when (exception.Failure == LocalAccountConfigurationFailure.Missing)
            { current = new(1, []); }

            var replacement = Validate(update(current));
            cancellationToken.ThrowIfCancellationRequested();
            temporary = Path.Combine(directory, $".users-{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                // The protected directory makes the file secure from the instant of creation.
                await JsonSerializer.SerializeAsync(stream, replacement, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            _accessPolicy.SecureFile(temporary);
            _accessPolicy.ValidateReadAccess(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_filePath)) File.Replace(temporary, _filePath, null);
            else File.Move(temporary, _filePath);
            temporary = null;
            _accessPolicy.ValidateReadAccess(_filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.StorageUnavailable); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(_filePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
            {
                if (timer.Elapsed >= TimeSpan.FromSeconds(5))
                    throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.Busy);
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    private static LocalAccountFile Validate(LocalAccountFile file)
    {
        if (file is null || file.SchemaVersion != 1 || file.Users is null) throw InvalidData();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<Guid>();
        var records = new List<LocalAccountRecord>();
        try
        {
            foreach (var user in file.Users)
            {
                if (user is null || user.UserId == Guid.Empty || !ids.Add(user.UserId)
                    || string.IsNullOrWhiteSpace(user.DisplayName)) throw InvalidData();
                var username = LocalAccountValidation.NormalizeUsername(user.Username);
                if (!names.Add(username)) throw InvalidData();
                LocalPasswordHasher.ValidateRecord(new(user.PasswordAlgorithm, user.Iterations,
                    Convert.FromBase64String(user.Salt), Convert.FromBase64String(user.PasswordHash)));
                records.Add(user with { Username = username });
            }
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        { throw InvalidData(); }
        return new(1, records.AsReadOnly());
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw InvalidData();

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidData();
        var properties = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Length != names.Length || properties.Distinct(StringComparer.Ordinal).Count() != names.Length
            || names.Any(name => !properties.Contains(name, StringComparer.Ordinal))) throw InvalidData();
    }

    private static LocalAccountConfigurationException InvalidData() => new(LocalAccountConfigurationFailure.InvalidData);
}

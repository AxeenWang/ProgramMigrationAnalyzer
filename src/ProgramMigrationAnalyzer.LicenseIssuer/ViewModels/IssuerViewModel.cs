using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using ProgramMigrationAnalyzer.LicenseIssuer.Services;

namespace ProgramMigrationAnalyzer.LicenseIssuer.ViewModels;

public enum IssuerAccountOperation { Create, Reset, Enable, Disable }
public sealed record IssuerAccountSummary(string Username, string DisplayName, bool IsEnabled);

public sealed class IssuerSecretSubmission : IDisposable
{
    private readonly SecureString _password;
    private readonly SecureString _confirmation;
    private bool _disposed;
    public IssuerSecretSubmission(SecureString password, SecureString confirmation)
    {
        _password = password.Copy();
        _password.MakeReadOnly();
        try { _confirmation = confirmation.Copy(); _confirmation.MakeReadOnly(); }
        catch { _password.Dispose(); throw; }
    }
    public SecureString CopyPassword()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var first = IntPtr.Zero; var second = IntPtr.Zero;
        try
        {
            first = Marshal.SecureStringToBSTR(_password);
            second = Marshal.SecureStringToBSTR(_confirmation);
            var difference = _password.Length ^ _confirmation.Length;
            for (var i = 0; i < Math.Min(_password.Length, _confirmation.Length); i++)
                difference |= Marshal.ReadInt16(first, i * 2) ^ Marshal.ReadInt16(second, i * 2);
            if (difference != 0) throw new ArgumentException("Password confirmation differs.");
            LocalAccountValidation.ValidateNewPassword(_password);
            return _password.Copy();
        }
        finally
        {
            if (first != IntPtr.Zero) Marshal.ZeroFreeBSTR(first);
            if (second != IntPtr.Zero) Marshal.ZeroFreeBSTR(second);
        }
    }
    public override string ToString() => nameof(IssuerSecretSubmission);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _password.Dispose(); _confirmation.Dispose();
    }
}

public sealed class IssuerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IssuerAccountEditor _editor = new();
    private readonly LicenseIssuanceService _issuance = new();
    private readonly ProtectedIssuerFileWriter _writer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private IssuerSigningKey? _key;
    private AuthorizationPayload? _payload;
    private bool _issued;
    private bool _closed;
    private bool _isBusy;
    private string _status = "請產生或載入公司的加密私鑰。登入密碼與私鑰保護密碼分開輸入。";
    public IssuerViewModel(IssuerSigningKey? signingKey = null) => _key = signingKey;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ClearSensitiveInputsRequested;
    public bool IsBusy => _isBusy;
    public bool CanEdit => !_closed && !_isBusy;
    public bool HasSigningKey => _key is not null;
    public string KeyId => _key is null ? "" : AuthorizationTrust.FromPublicKeyPem(_key.ExportPublicPem()).KeyId!;
    public string StatusMessage => _status;
    public string AuthorizationSummary => _payload is null ? "尚未建立授權" : $"授權 {_payload.AuthorizationId}，版本 {_payload.Revision}" + (_issued ? "" : "，尚未簽發");
    public IReadOnlyList<IssuerAccountSummary> Accounts => _payload?.Users.Select(u => new IssuerAccountSummary(u.Username,u.DisplayName,u.IsEnabled)).ToArray() ?? [];
    public Task? PendingOperation { get; private set; }
    public Task ApplyAccountAsync(IssuerAccountOperation operation, string username, string displayName, IssuerSecretSubmission? submission) =>
        StartAsync(async ct =>
        {
            if (!Enum.IsDefined(operation)) throw new ArgumentException("Unsupported account operation.");
            RequireKey();
            var current = _payload;
            if (operation is IssuerAccountOperation.Create or IssuerAccountOperation.Reset)
            {
                using var password = submission?.CopyPassword() ?? throw new ArgumentException("Password required.");
                var changed = await Task.Run(() => operation switch
                {
                    IssuerAccountOperation.Create when current is null => _issuance.CreateNew([_editor.CreateAccount(username,displayName,password)],DateTimeOffset.UtcNow),
                    IssuerAccountOperation.Create => _editor.Create(current!,username,displayName,password),
                    _ => _editor.Reset(current ?? throw new ArgumentException("Open an authorization first."),username,displayName,password)
                }, ct);
                ct.ThrowIfCancellationRequested();
                _payload = changed;
            }
            else
            {
                ct.ThrowIfCancellationRequested();
                _payload = _editor.SetEnabled(current ?? throw new ArgumentException("Open an authorization first."),
                    username, operation == IssuerAccountOperation.Enable);
            }
            NotifyState();
        }, "帳號清單已更新，請匯出新的授權 Key。");

    public Task GenerateAndSaveKeyAsync(string privatePath, string publicPath, IssuerSecretSubmission submission) =>
        StartAsync(async ct =>
        {
            if (Path.GetFullPath(privatePath).Equals(Path.GetFullPath(publicPath),StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Private and public keys require distinct destinations.");
            using var password = submission.CopyPassword();
            IssuerSigningKey? generated = null;
            byte[]? encrypted = null;
            try
            {
                generated = await Task.Run(IssuerSigningKey.Generate, ct);
                encrypted = await Task.Run(() => generated.ExportEncrypted(password), ct);
                ct.ThrowIfCancellationRequested();
                await _writer.WriteNewPrivateKeyAsync(privatePath, encrypted, ct);
                await _writer.WriteAuthorizationAsync(publicPath, Encoding.UTF8.GetBytes(generated.ExportPublicPem()), ct);
                ct.ThrowIfCancellationRequested();
                _key?.Dispose(); _key = generated; generated = null;
                _payload = null; _issued = false; NotifyState();
            }
            finally { generated?.Dispose(); if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted); }
        }, "加密私鑰與公鑰已儲存，請妥善備份私鑰及其保護密碼。");

    public Task LoadKeyAsync(string path, IssuerSecretSubmission submission) =>
        StartAsync(async ct =>
        {
            using var password = submission.CopyPassword();
            var bytes = await ReadBoundedAsync(path, 32768, ct);
            IssuerSigningKey? loaded = null;
            try
            {
                loaded = await Task.Run(() => IssuerSigningKey.ImportEncrypted(bytes,password), ct);
                ct.ThrowIfCancellationRequested();
                _key?.Dispose(); _key = loaded; loaded = null;
                _payload = null; _issued = false; NotifyState();
            }
            finally { loaded?.Dispose(); CryptographicOperations.ZeroMemory(bytes); }
        }, "公司私鑰已載入，請建立帳號或開啟既有授權 Key。");

    public Task ExportPublicAsync(string path) => StartAsync(async ct =>
    {
        var key = RequireKey();
        await _writer.WriteAuthorizationAsync(path,Encoding.UTF8.GetBytes(key.ExportPublicPem()),ct);
    }, "公鑰已匯出，可用於客戶端建置。");

    public Task OpenAuthorizationAsync(string path) => StartAsync(async ct =>
    {
        var key = RequireKey();
        var verifier = new SignedAuthorizationVerifier(AuthorizationTrust.FromPublicKeyPem(key.ExportPublicPem()));
        await using var stream = File.OpenRead(path);
        var verified = await verifier.ReadAndVerifyAsync(stream,ct);
        ct.ThrowIfCancellationRequested();
        _payload = verified.Payload; _issued = true; NotifyState();
    }, "既有授權已驗章並載入，更新後重新簽發會增加版本。");

    public Task ExportAuthorizationAsync(string path) => StartAsync(async ct =>
    {
        var key = RequireKey();
        var current = _payload ?? throw new ArgumentException("At least one account is required.");
        var candidate = _issued ? _issuance.Reissue(current,DateTimeOffset.UtcNow) : current with { IssuedAtUtc = DateTimeOffset.UtcNow };
        var envelope = await Task.Run(() => key.SignPayload(candidate),ct);
        ct.ThrowIfCancellationRequested();
        await _writer.WriteAuthorizationAsync(path,envelope,ct);
        ct.ThrowIfCancellationRequested();
        _payload = candidate; _issued = true; NotifyState();
    }, "授權 Key 已簽發，僅包含帳號及密碼雜湊。");

    private Task StartAsync(Func<CancellationToken,Task> operation, string success)
    {
        if (!CanEdit) return Task.CompletedTask;
        PendingOperation = RunAsync(operation,success);
        return PendingOperation;
    }
    private async Task RunAsync(Func<CancellationToken,Task> operation, string success)
    {
        _isBusy = true; NotifyState();
        ClearSensitiveInputsRequested?.Invoke(this,EventArgs.Empty);
        try
        {
            await operation(_lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            if (!_closed) _status = success;
        }
        catch (OperationCanceledException) { if (!_closed) _status = "操作已取消。"; }
        catch (ArgumentException) { if (!_closed) _status = "請確認帳號、顯示名稱及兩次一致的 8～128 字元密碼，私鑰須存於 Git 專案之外。"; }
        catch (CryptographicException) { if (!_closed) _status = "私鑰或保護密碼不正確，請重新確認。"; }
        catch (AuthorizationException) { if (!_closed) _status = "授權 Key 無效，或不是由目前公司金鑰簽發。"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { if (!_closed) _status = "檔案無法安全讀取或儲存，請確認路徑、存取權限及是否已存在。"; }
        finally
        {
            _isBusy = false;
            if (_closed) ReleaseState();
            NotifyState();
            ClearSensitiveInputsRequested?.Invoke(this,EventArgs.Empty);
        }
    }
    private IssuerSigningKey RequireKey() => _key ?? throw new ArgumentException("Load the company key first.");
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(IsBusy),nameof(CanEdit),nameof(HasSigningKey),nameof(KeyId),nameof(StatusMessage),nameof(Accounts),nameof(AuthorizationSummary) })
            PropertyChanged?.Invoke(this,new(property));
    }
    private static async Task<byte[]> ReadBoundedAsync(string path,int maximum,CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0,Math.Min(buffer.Length,maximum+1-(int)output.Length)),ct);
            if (read == 0) return output.ToArray();
            if (output.Length+read > maximum) throw new ArgumentException("Input exceeds permitted size.");
            await output.WriteAsync(buffer.AsMemory(0,read),ct);
        }
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _lifetime.Cancel();
        ClearSensitiveInputsRequested?.Invoke(this,EventArgs.Empty);
        if (!_isBusy) ReleaseState();
        NotifyState();
    }
    private void ReleaseState() { _key?.Dispose(); _key = null; _payload = null; _lifetime.Dispose(); }
}

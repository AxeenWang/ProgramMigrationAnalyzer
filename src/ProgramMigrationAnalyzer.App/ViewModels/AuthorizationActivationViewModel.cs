using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProgramMigrationAnalyzer.App.Services;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
namespace ProgramMigrationAnalyzer.App.ViewModels;
internal sealed class AuthorizationActivationViewModel : ObservableObject, IDisposable
{
    private readonly SignedLocalAccountStore _store;
    private readonly SignedAuthorizationVerifier _verifier;
    private readonly IAuthorizationImportLauncher _launcher;
    private readonly ILocalAccountAccessPolicy? _policy;
    private CancellationTokenSource? _operation;
    private string? _path;
    private string? _fingerprint;
    private Guid? _replacementId;
    private long _generation;
    private bool _closed, _busy, _confirmReplacement;
    private string _summary = "", _replacementSummary = "";
    private string _status = "請選擇公司簽發的授權 Key。匯入後仍需輸入帳號與密碼登入。";
    internal AuthorizationActivationViewModel(SignedLocalAccountStore store, SignedAuthorizationVerifier verifier,
        IAuthorizationImportLauncher launcher, bool elevatedImport = false, ILocalAccountAccessPolicy? policy = null)
    {
        _store = store; _verifier = verifier; _launcher = launcher; _policy = policy; IsElevatedImport = elevatedImport;
        ImportCommand = new AsyncRelayCommand(ct => ImportAsync(ct), () => CanImport);
        CancelCommand = new RelayCommand(Cancel, () => !_closed);
        if (elevatedImport && policy?.IsElevatedAdministrator != true) _status = "匯入需要管理員權限，請關閉此視窗並重新操作。";
    }
    public bool IsElevatedImport { get; }
    public bool CanEdit => !_closed && !IsBusy;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); NotifyAvailability(); } }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string ReplacementSummary { get => _replacementSummary; private set => SetProperty(ref _replacementSummary, value); }
    public string StatusMessage { get => _status; private set => SetProperty(ref _status, value); }
    public bool RequiresReplacementConfirmation => _replacementId is not null;
    public bool ConfirmReplacement { get => _confirmReplacement; set { SetProperty(ref _confirmReplacement, value); NotifyAvailability(); } }
    public bool CanImport => CanEdit && _path is not null && _fingerprint is not null
        && (!IsElevatedImport || _policy?.IsElevatedAdministrator == true)
        && (!RequiresReplacementConfirmation || ConfirmReplacement);
    public IAsyncRelayCommand ImportCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public event Action? Activated;
    public event Action? Cancelled;
    internal Task PreviewAsync(string path, CancellationToken ct = default) => RunAsync(async (generation, token) =>
    {
        ClearSelection();
        if (IsElevatedImport && _policy?.IsElevatedAdministrator != true) return;
        var candidate = await ReadCandidateAsync(path, token);
        Guid? replacementId = null;
        string replacementSummary = "";
        if (IsElevatedImport)
        {
            try
            {
                var current = await _store.ReadAsync(token);
                if (current.Payload.AuthorizationId != candidate.Payload.AuthorizationId)
                {
                    replacementId = current.Payload.AuthorizationId;
                    replacementSummary = $"將替換目前授權 {current.Payload.AuthorizationId}，版本 {current.Payload.Revision}。";
                }
            }
            catch (AuthorizationException error) when (error.Failure is AuthorizationFailure.Missing or AuthorizationFailure.InvalidData
                or AuthorizationFailure.InvalidSignature or AuthorizationFailure.UntrustedKey) { }
        }
        if (!IsCurrent(generation, token)) return;
        _path = Path.GetFullPath(path); _fingerprint = candidate.PayloadFingerprint; _replacementId = replacementId;
        ReplacementSummary = replacementSummary;
        Summary = $"授權：{candidate.Payload.AuthorizationId}\n版本：{candidate.Payload.Revision}\n簽發時間：{candidate.Payload.IssuedAtUtc:O}\n帳號數：{candidate.Payload.Users.Count}\n公司公鑰：{candidate.KeyId}";
        StatusMessage = IsElevatedImport ? "請確認以上授權內容，按匯入後寫入本機。" : "公司簽章已驗證，按匯入後請完成 Windows 權限確認。";
        NotifyAvailability();
    }, ct);
    internal Task ImportAsync(CancellationToken ct = default)
    {
        if (!CanImport) return Task.CompletedTask;
        var path = _path!; var fingerprint = _fingerprint!; var replacement = ConfirmReplacement ? _replacementId : null;
        return RunAsync(async (generation, token) =>
        {
            var candidate = await ReadCandidateAsync(path, token);
            if (!IsCurrent(generation, token)) return;
            if (candidate.PayloadFingerprint != fingerprint)
            { ClearSelection(); StatusMessage = "授權檔已變更，請重新選擇並確認。"; return; }
            StatusMessage = "授權匯入中…";
            if (IsElevatedImport) await _store.ImportAsync(candidate.CopyEnvelopeBytes(), replacement, token);
            else
            {
                var result = await _launcher.ImportAsync(path, token);
                if (!IsCurrent(generation, token)) return;
                if (result != AuthorizationImportProcessResult.Succeeded)
                {
                    StatusMessage = result == AuthorizationImportProcessResult.Canceled ? "已取消權限確認或匯入，尚未登入。" : "授權未能匯入，請重新操作或聯絡管理者。";
                    return;
                }
            }
            var installed = await _store.ReadAsync(token);
            if (!IsCurrent(generation, token)) return;
            if (installed.PayloadFingerprint != fingerprint)
            { ClearSelection(); StatusMessage = "已安裝授權與選擇內容不同，請重新確認。"; return; }
            StatusMessage = "授權已匯入，請使用公司提供的帳號與密碼登入。";
            Activated?.Invoke();
        }, ct);
    }
    private async Task<VerifiedAuthorization> ReadCandidateAsync(string path, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path)) throw new AuthorizationException(AuthorizationFailure.InvalidData);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await _verifier.ReadAndVerifyAsync(stream, ct);
    }
    private async Task RunAsync(Func<long, CancellationToken, Task> action, CancellationToken ct)
    {
        if (!CanEdit) return;
        var generation = ++_generation;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _operation = operation; IsBusy = true;
        try { await action(generation, operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception error) when (error is AuthorizationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!IsCurrent(generation, operation.Token)) return;
            ClearSelection();
            StatusMessage = error is AuthorizationException { Failure: AuthorizationFailure.UntrustedKey }
                ? "此版本未設定有效公司公鑰，或 Key 不屬於本公司，請聯絡管理者。"
                : "授權無法安全讀取或匯入，請重新選擇有效 Key 或聯絡管理者。";
        }
        finally { if (ReferenceEquals(_operation, operation)) _operation = null; IsBusy = false; }
    }
    private bool IsCurrent(long generation, CancellationToken ct) => !_closed && _generation == generation && !ct.IsCancellationRequested;
    private void ClearSelection()
    {
        _path = null; _fingerprint = null; _replacementId = null; Summary = ""; ReplacementSummary = "";
        ConfirmReplacement = false; NotifyAvailability();
    }
    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanImport)); OnPropertyChanged(nameof(RequiresReplacementConfirmation));
        ImportCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
    }
    public void Cancel()
    {
        if (_closed) return;
        _closed = true; ++_generation; _operation?.Cancel(); ClearSelection(); Cancelled?.Invoke();
    }
    public void Dispose() { Cancel(); Activated = null; Cancelled = null; }
}

using System.Security.Cryptography;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ProgramMigrationAnalyzer.LicenseIssuer.ViewModels;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using ProgramMigrationAnalyzer.LicenseIssuer.Services;
namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class IssuerChecks
{
    public static void Run() => CheckSupport.Run(
        (nameof(EncryptedKeyMemoryRoundTrip), EncryptedKeyMemoryRoundTrip),
        (nameof(WrongProtectionPasswordFails), WrongProtectionPasswordFails),
        (nameof(IndependentSecretsAndEightCharacterSymbols), IndependentSecretsAndEightCharacterSymbols),
        (nameof(IssueCreateResetDisableLifecycle), IssueCreateResetDisableLifecycle),
        (nameof(RevisionOverflowAndInvalidAccounts), RevisionOverflowAndInvalidAccounts),
        (nameof(IssuerUiClearsSecretsAndRejectsDuplicateSubmit), IssuerUiClearsSecretsAndRejectsDuplicateSubmit),
        (nameof(PrivateKeyCannotBeWrittenIntoRepository), PrivateKeyCannotBeWrittenIntoRepository),
        (nameof(AuthorizationWritePreservesCanceledFile), () => AuthorizationWritePreservesCanceledFile().GetAwaiter().GetResult()),
        (nameof(IssuerViewModelIssuesRealLoginLifecycle), () => IssuerViewModelIssuesRealLoginLifecycle().GetAwaiter().GetResult()),
        (nameof(AuthorizationExportCannotReplacePrivateKey), () => AuthorizationExportCannotReplacePrivateKey().GetAwaiter().GetResult()),
        (nameof(SharingConflictKeepsPriorAuthorization), () => SharingConflictKeepsPriorAuthorization().GetAwaiter().GetResult()),
        (nameof(UnsupportedOperationCannotDisableAccount), () => UnsupportedOperationCannotDisableAccount().GetAwaiter().GetResult()));

    private static async Task UnsupportedOperationCannotDisableAccount()
    {
        using var key = IssuerSigningKey.Generate(); using var vm = new IssuerViewModel(key);
        using var password = CheckSupport.Password("Test@!#1");
        using var submission = new IssuerSecretSubmission(password,password);
        await vm.ApplyAccountAsync(IssuerAccountOperation.Create,"internal.one","Internal",submission);
        await vm.ApplyAccountAsync((IssuerAccountOperation)99,"internal.one","",null);
        CheckSupport.Check(vm.Accounts[0].IsEnabled,"Unsupported UI operation silently disabled an account.");
    }

    private static async Task AuthorizationExportCannotReplacePrivateKey()
    {
        using var fixture = new SignedAccountFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        // Invalid marker-only fixture, never an exported private key.
        var marker = System.Text.Encoding.ASCII.GetBytes("-----BEGIN ENCRYPTED PRIVATE KEY-----\nAA==\n-----END ENCRYPTED PRIVATE KEY-----");
        await File.WriteAllBytesAsync(fixture.FilePath,marker);
        await StoreChecks.Expect<IOException>(() => new ProtectedIssuerFileWriter().WriteAuthorizationAsync(fixture.FilePath,fixture.Key.Issue()));
        CheckSupport.Check((await File.ReadAllBytesAsync(fixture.FilePath)).SequenceEqual(marker),"Authorization export overwrote a private key destination.");
    }
    private static async Task SharingConflictKeepsPriorAuthorization()
    {
        using var fixture = new SignedAccountFixture(); Directory.CreateDirectory(fixture.DirectoryPath);
        var writer = new ProtectedIssuerFileWriter(); var original = fixture.Key.Issue();
        await writer.WriteAuthorizationAsync(fixture.FilePath,original);
        using (var held = new FileStream(fixture.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
            await StoreChecks.Expect<IOException>(() => writer.WriteAuthorizationAsync(fixture.FilePath,fixture.Key.Issue(fixture.Key.Payload(2))));
        CheckSupport.Check((await File.ReadAllBytesAsync(fixture.FilePath)).SequenceEqual(original)
            && !Directory.EnumerateFiles(fixture.DirectoryPath,"*.tmp").Any(),"Failed issuer replacement damaged prior Key or left scratch files.");
    }

    private static void IssuerUiClearsSecretsAndRejectsDuplicateSubmit() => CheckSupport.RunStaChild("--issuer-window-check");
    internal static int RunWindowChild()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var key = IssuerSigningKey.Generate();
        using var vm = new IssuerViewModel(key);
        var window = new ProgramMigrationAnalyzer.LicenseIssuer.IssuerWindow(vm) { ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        var result = 0;
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                foreach (var name in new[] { "PasswordInput", "ConfirmationInput", "ProtectionInput", "ProtectionConfirmationInput" })
                    CheckSupport.Check(window.FindName(name) is PasswordBox, "Issuer secret inputs are not available.");
                var password = (PasswordBox)window.FindName("PasswordInput");
                var confirmation = (PasswordBox)window.FindName("ConfirmationInput");
                var protection = (PasswordBox)window.FindName("ProtectionInput");
                var protectionConfirmation = (PasswordBox)window.FindName("ProtectionConfirmationInput");
                var submit = (Button)window.FindName("SubmitButton");
                var mode = (ComboBox)window.FindName("OperationInput");
                ((TextBox)window.FindName("UsernameInput")).Text = "internal.one";
                ((TextBox)window.FindName("DisplayNameInput")).Text = "Internal user";
                password.Password = "Ab@!#9x?"; confirmation.Password = "Different";
                protection.Password = protectionConfirmation.Password = "Protect!1";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await vm.PendingOperation!;
                CheckSupport.Check(vm.Accounts.Count == 0 && vm.StatusMessage.Contains("一致"),"Mismatched account secrets were accepted.");
                CheckSupport.Check(new[] { password,confirmation,protection,protectionConfirmation }.All(box => box.Password.Length == 0),
                    "Both independent secret pairs must clear after rejection.");
                password.Password = confirmation.Password = "Ab@!#9x?";
                protection.Password = protectionConfirmation.Password = "Protect!1";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var pending = vm.PendingOperation;
                CheckSupport.Check(vm.IsBusy && !password.IsEnabled && !submit.IsEnabled,"Issuer submission must disable duplicate editing.");
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                CheckSupport.Check(ReferenceEquals(pending,vm.PendingOperation),"Duplicate submit started a second operation.");
                await pending!;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                CheckSupport.Check(vm.Accounts.Count == 1 && new[] { password,confirmation,protection,protectionConfirmation }.All(box => box.Password.Length == 0),
                    "Issuer must add one account and clear both secret pairs.");
                using (var fixture = new SignedAccountFixture())
                {
                    var imagePath = Path.Combine(Directory.GetParent(fixture.DirectoryPath)!.Parent!.FullName,"issuer-window.png");
                    var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,
                        96,96,System.Windows.Media.PixelFormats.Pbgra32);
                    image.Render(window);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using var output = File.Create(imagePath); encoder.Save(output);
                }
                CheckSupport.Check(!typeof(IssuerViewModel).GetProperties().Any(property => property.PropertyType == typeof(System.Security.SecureString)),
                    "Issuer observable properties retain a secret.");
                password.Password = confirmation.Password = "Discard@1";
                mode.SelectedIndex = 3;
                CheckSupport.Check(!password.IsEnabled && password.Password.Length == 0 && confirmation.Password.Length == 0,"Changing operation did not clear/disable secrets.");
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await vm.PendingOperation!;
                CheckSupport.Check(!vm.Accounts[0].IsEnabled,"Disable operation changed no account.");
                mode.SelectedIndex = 1;
                password.Password = confirmation.Password = "Changed@1";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pending = vm.PendingOperation;
                window.Close();
                await pending!;
                CheckSupport.Check(!vm.CanEdit && vm.Accounts.Count == 0 && app.Windows.Count == 0
                    && new[] { password,confirmation,protection,protectionConfirmation }.All(box => box.Password.Length == 0),
                    "Closing must cancel pending edits, clear memory and secrets, and never open an analyzer.");
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); result = 1; }
            finally { window.Close(); app.Shutdown(); }
        }));
        window.Show();
        app.Run();
        return result;
    }

    private static async Task IssuerViewModelIssuesRealLoginLifecycle()
    {
        using var fixture = new SignedAccountFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        using var key = IssuerSigningKey.Generate();
        var verifier = new SignedAuthorizationVerifier(AuthorizationTrust.FromPublicKeyPem(key.ExportPublicPem()));
        using var vm = new IssuerViewModel(key);
        using var first = CheckSupport.Password("Test@!#1");
        using var next = CheckSupport.Password("New@!#12");
        using (var submission = new IssuerSecretSubmission(first,first))
            await vm.ApplyAccountAsync(IssuerAccountOperation.Create,"internal.one","Internal",submission);
        await vm.ExportAuthorizationAsync(fixture.FilePath);
        var store = new SignedLocalAccountStore(fixture.FilePath,fixture.Policy,verifier);
        var provider = new SignedLocalAuthenticationService(store,new());
        using (var request = new ProgramMigrationAnalyzer.Core.Authentication.LoginRequest(
            ProgramMigrationAnalyzer.Core.Authentication.LoginRequestKind.Credentials,"internal.one",first))
            CheckSupport.Check((await provider.AuthenticateAsync(request)).IsSuccess,"Real issuer-to-client login failed.");
        var initial = await store.ReadAsync();
        using (var submission = new IssuerSecretSubmission(next,next))
            await vm.ApplyAccountAsync(IssuerAccountOperation.Reset,"internal.one","Updated",submission);
        await vm.ApplyAccountAsync(IssuerAccountOperation.Disable,"internal.one","",null);
        await vm.ExportAuthorizationAsync(fixture.FilePath);
        var disabled = await store.ReadAsync();
        CheckSupport.Check(disabled.Payload.Revision == 2 && disabled.Payload.AuthorizationId == initial.Payload.AuthorizationId
            && !disabled.Payload.Users[0].IsEnabled && disabled.Payload.Users[0].UserId == initial.Payload.Users[0].UserId,
            "UI reissue lost authorization or account identity.");
        await vm.ApplyAccountAsync(IssuerAccountOperation.Enable,"internal.one","",null);
        await vm.ExportAuthorizationAsync(fixture.FilePath);
        using var oldRequest = new ProgramMigrationAnalyzer.Core.Authentication.LoginRequest(
            ProgramMigrationAnalyzer.Core.Authentication.LoginRequestKind.Credentials,"internal.one",first);
        using var newRequest = new ProgramMigrationAnalyzer.Core.Authentication.LoginRequest(
            ProgramMigrationAnalyzer.Core.Authentication.LoginRequestKind.Credentials,"internal.one",next);
        CheckSupport.Check(!(await provider.AuthenticateAsync(oldRequest)).IsSuccess && (await provider.AuthenticateAsync(newRequest)).IsSuccess
            && (await store.ReadAsync()).Payload.Revision == 3,"UI reset did not revoke the old password on next login.");
    }

    private static void PrivateKeyCannotBeWrittenIntoRepository()
    {
        using var fixture = new SignedAccountFixture();
        using var key = IssuerSigningKey.Generate();
        using var secret = CheckSupport.Password("Protect!1");
        var bytes = key.ExportEncrypted(secret);
        try
        {
            CheckSupport.Throws<ArgumentException>(() => new ProtectedIssuerFileWriter()
                .WriteNewPrivateKeyAsync(System.IO.Path.Combine(fixture.DirectoryPath,"private-key.pem"),bytes).GetAwaiter().GetResult());
            CheckSupport.Check(!System.IO.Directory.Exists(fixture.DirectoryPath), "Rejected private write created repository content.");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static async Task AuthorizationWritePreservesCanceledFile()
    {
        using var fixture = new SignedAccountFixture();
        System.IO.Directory.CreateDirectory(fixture.DirectoryPath);
        var writer = new ProtectedIssuerFileWriter();
        var original = fixture.Key.Issue();
        await writer.WriteAuthorizationAsync(fixture.FilePath, original);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await StoreChecks.Expect<OperationCanceledException>(() => writer.WriteAuthorizationAsync(fixture.FilePath,fixture.Key.Issue(),canceled.Token));
        CheckSupport.Check((await System.IO.File.ReadAllBytesAsync(fixture.FilePath)).SequenceEqual(original),"Canceled issuer write changed prior Key.");
        await writer.WriteAuthorizationAsync(fixture.FilePath,fixture.Key.Issue(fixture.Key.Payload(2)));
        CheckSupport.Check(fixture.Key.Verifier.Verify(await System.IO.File.ReadAllBytesAsync(fixture.FilePath)).Payload.Revision == 2,
            "Issuer output cannot be atomically updated.");
        var security = new System.IO.FileInfo(fixture.FilePath).GetAccessControl();
        CheckSupport.Check(security.AreAccessRulesProtected,"Issuer output still inherits writable ACL.");
        CheckSupport.Check(!System.IO.Directory.EnumerateFiles(fixture.DirectoryPath,"*.tmp").Any(),"Issuer write left scratch files.");
    }

    private static void EncryptedKeyMemoryRoundTrip()
    {
        using var key = IssuerSigningKey.Generate();
        using var secret = CheckSupport.Password("Protect!1");
        var bytes = key.ExportEncrypted(secret);
        try
        {
            using var reloaded = IssuerSigningKey.ImportEncrypted(bytes, secret);
            CheckSupport.Check(reloaded.ExportPublicPem() == key.ExportPublicPem(), "Encrypted key roundtrip changed trust.");
            var payload = NewPayload();
            var verifier = new SignedAuthorizationVerifier(AuthorizationTrust.FromPublicKeyPem(key.ExportPublicPem()));
            CheckSupport.Check(verifier.Verify(reloaded.SignPayload(payload)).Payload.AuthorizationId == payload.AuthorizationId,
                "Reloaded signing key cannot sign an interoperable package.");
            CheckSupport.Check(System.Text.Encoding.UTF8.GetString(bytes).StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----"),
                "Private key must use encrypted PKCS8 PEM.");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void WrongProtectionPasswordFails()
    {
        using var key = IssuerSigningKey.Generate();
        using var secret = CheckSupport.Password("Protect!1"); using var wrong = CheckSupport.Password("Incorrect");
        var bytes = key.ExportEncrypted(secret);
        try { CheckSupport.Throws<CryptographicException>(() => IssuerSigningKey.ImportEncrypted(bytes, wrong)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void IndependentSecretsAndEightCharacterSymbols()
    {
        using var key = IssuerSigningKey.Generate();
        using var protection = CheckSupport.Password(" 保護密碼@!# ");
        using var login = CheckSupport.Password("@!#abc12");
        var user = new IssuerAccountEditor().CreateAccount(" Internal.One ", "Internal user", login);
        var hash = new PasswordHashRecord(user.PasswordAlgorithm,user.Iterations,Convert.FromBase64String(user.Salt),Convert.FromBase64String(user.PasswordHash));
        CheckSupport.Check(new LocalPasswordHasher().Verify(login,hash) && !new LocalPasswordHasher().Verify(protection,hash),
            "Protection and login secrets were conflated.");
        var bytes = key.ExportEncrypted(protection);
        try
        {
            using var reloaded = IssuerSigningKey.ImportEncrypted(bytes,protection);
            CheckSupport.Throws<CryptographicException>(() => IssuerSigningKey.ImportEncrypted(bytes,login));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        using var spaced = CheckSupport.Password(" abc@!#12 ");
        var exact = new IssuerAccountEditor().CreateAccount("another.one","Unicode 空白",spaced);
        using var trimmed = CheckSupport.Password("abc@!#12");
        CheckSupport.Check(!new LocalPasswordHasher().Verify(trimmed,new(exact.PasswordAlgorithm,exact.Iterations,
            Convert.FromBase64String(exact.Salt),Convert.FromBase64String(exact.PasswordHash))), "Password spaces were trimmed.");
    }
    private static void IssueCreateResetDisableLifecycle()
    {
        using var key = IssuerSigningKey.Generate(); var editor = new IssuerAccountEditor(); var issuance = new LicenseIssuanceService();
        var first = NewPayload(); var original = first.Users[0];
        using var password = CheckSupport.Password("New@!#12");
        var edited = editor.Reset(first, original.Username, "Updated", password);
        CheckSupport.Check(edited.Revision == 1 && edited.Users[0].UserId == original.UserId && edited.Users[0].Salt != original.Salt,
            "Editing should preserve revision and account id, refresh reset salt.");
        edited = editor.SetEnabled(edited, original.Username, false);
        edited = editor.Create(edited, "second.one", "Second", password);
        var reissued = issuance.Reissue(edited, DateTimeOffset.UtcNow);
        var verified = new SignedAuthorizationVerifier(AuthorizationTrust.FromPublicKeyPem(key.ExportPublicPem())).Verify(key.SignPayload(reissued));
        CheckSupport.Check(verified.Payload.Revision == 2 && verified.Payload.AuthorizationId == first.AuthorizationId
            && !verified.Payload.Users[0].IsEnabled && verified.Payload.Users.Count == 2, "Reissued lifecycle is incorrect.");
    }
    private static void RevisionOverflowAndInvalidAccounts()
    {
        var issuance = new LicenseIssuanceService(); var editor = new IssuerAccountEditor(); var payload = NewPayload();
        CheckSupport.Throws<OverflowException>(() => issuance.Reissue(payload with { Revision = int.MaxValue },DateTimeOffset.UtcNow));
        using var shortPassword = CheckSupport.Password("short");
        CheckSupport.Throws<ArgumentException>(() => editor.CreateAccount("valid.one","Name",shortPassword));
        using var password = CheckSupport.Password("@!#abc12");
        CheckSupport.Throws<ArgumentException>(() => editor.Create(payload," INTERNAL.ONE ","Duplicate",password));
        CheckSupport.Throws<ArgumentException>(() => editor.CreateAccount("valid.one","  ",password));
        CheckSupport.Throws<ArgumentException>(() => issuance.CreateNew([],DateTimeOffset.UtcNow));
        CheckSupport.Throws<ArgumentException>(() => editor.SetEnabled(payload,"missing",false));
    }
    internal static AuthorizationPayload NewPayload()
    {
        using var password = CheckSupport.Password("Test@!#1");
        return new LicenseIssuanceService().CreateNew([new IssuerAccountEditor().CreateAccount("internal.one","Internal user",password)],DateTimeOffset.UtcNow);
    }
}

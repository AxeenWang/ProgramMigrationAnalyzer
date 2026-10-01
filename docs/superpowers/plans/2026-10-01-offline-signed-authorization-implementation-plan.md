# 離線簽章授權 Phase 5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox syntax for tracking. 使用者已指定同一實作者順序執行，不使用代理分工。

**Goal:** 以公司簽發的離線 Key 初始化及維護本機帳號，每次登入驗證公司簽章與帳密，阻止正常客戶端自行建帳或重設。

**Architecture:** Infrastructure 提供嚴格的簽章 envelope 驗證、原子匯入與 signed local provider。客戶端只內建公司公鑰，首次啟用與授權更新走獨立提升權限匯入模式。新的公司用 WPF LicenseIssuer 保管加密私鑰、維護帳號清單及簽發 Key，與客戶端分開發布。

**Tech Stack:** C#、.NET 10、Windows WPF、既有 MVVM、System.Text.Json、內建 ECDsa／SHA-256／PBKDF2，沿用現有 Windows ACL policy，不新增 NuGet 套件。

**Spec:** [核准規格](../specs/2026-10-01-offline-signed-authorization-design.md)，已於 2026-10-01 核准。兩份核准文件已在第一個 Task 納入正式 docs。

**狀態:** 使用者已指示依本計畫作業。Phase 5 分支 `codex/offline-authorization-phase-5` 從 `7513324` 開工，P5-T1 已完成並推送 5a791c6，P5-T2 已完成並推送 52fcb92，P5-T3 已完成程式與驗證，待提交／推送，P5-T4～T6 尚未開始。Active Target 為 `Projects/ProgramMigrationAnalyzer`，Target Lock enabled。Git root 已依使用者授權移回 `D:/AxeenWorld/CodeLab/Projects/ProgramMigrationAnalyzer`，CodeLab-managed 模式。Phase 1～4 登入成果、T1／T2 及未提交 T3 已完整回到正式目錄，舊 worktree 保留為非作業用復原副本。

## Global Constraints

- 同一 Key 可跨主機使用，不綁定電腦，不設定到期日，不連線外部認證服務。
- 密碼 8～128 個 Unicode scalar values，允許 `@`、`!`、`#`、空白及 Unicode，原樣比較。帳號 3～64 ASCII 英文字母、數字、點、底線、連字號，Trim 後 invariant 小寫。
- 密碼 PBKDF2-HMAC-SHA256，新建 600,000 iterations，驗證支援 600,000～2,000,000，16 bytes salt、32 bytes hash，FixedTimeEquals。
- 簽章 `ECDSA-P256-SHA256-P1363`，nistP256、SHA-256、固定 64 bytes P1363。keyId 為 SPKI DER 的 SHA-256 大寫 64 位十六進位。
- envelope formatVersion 1，payload schemaVersion 2、productId `ProgramMigrationAnalyzer`，revision 1～2,147,483,647，users 1～1,000。UTF-8、無 BOM，大小上限 4 MiB、JSON 深度 16。
- 簽章 header 是 `PMA-OFFLINE-AUTH/v1\nECDSA-P256-SHA256-P1363\n<keyId>\n` 的 UTF-8 bytes 後接原始 payload bytes。驗證後才解析 payload，拒絕重複／未知欄位、非法 UTF-8／Base64、尾隨資料及不支援格式。
- 私鑰只能在公司發證工具使用，加密 PKCS#8 PEM 採 AES-256-CBC、PBKDF2-HMAC-SHA256、600,000 iterations。保護密碼另行遮罩輸入，不透過參數、環境變數或 log。
- 正式帳號路徑固定 `%ProgramData%/ProgramMigrationAnalyzer/auth/users.json`。Administrators／SYSTEM FullControl，Users 目錄 ReadAndExecute、檔案 Read，不修改 ProgramData 或磁碟根 ACL。
- 正式 App 只接受無參數或 `--import-authorization <完整路徑>`，舊管理入口與登入 bypass 拒絕。匯入不等於登入，每次登入都重讀驗章。
- unsigned schema 1 不自動放行或轉換，匯入失敗不損壞舊檔。停用／重設於下次登入生效，既有 session 不即時撤銷，output 保留。
- 測試私鑰只在記憶體即時產生。測試帳號檔、Key、log 只放 `.codex-tmp/YYYY-MM-DD_<task-name>/`，不提交，不碰真實 ProgramData。
- 發證工具、私鑰與實際 Key 不進入客戶端 publish。沒有有效公司公鑰的 publish 必須失敗，開發 build 必須保持未授權，沒有測試信任根或自動 fallback。
- 真實部署需取得指定電腦的新版本帳號替換／ACL 授權，正式私鑰與帳密由使用者操作。部署未完成不送 ready PR，不把隔離測試寫成真實部署通過。

## Review Focus

1. 大檔或檔案讀取中持續增長，仍在 4 MiB 上限停止且不配置無界 buffer，由 T1 `GrowingStreamIsBounded`、T2 匯入檢查覆蓋。
2. UI 確認後候選檔或目標授權被換掉，提升程序重新驗章，替換確認綁目前 authorizationId，由 T2 `ReplacementConfirmationUsesLockedCurrentId`、T4 `CandidateChangedBeforeElevation` 覆蓋。
3. UAC 取消、父視窗關閉或子程序晚到成功，不能開主畫面或自動登入，由 T4 `CanceledElevationAndLateCompletion` 覆蓋。
4. Unicode、空白及八字元符號密碼，私鑰保護密碼和登入密碼不互相代用，由 T3 `IndependentSecretsAndEightCharacterSymbols` 覆蓋。
5. 正式根目錄 publish、公鑰路徑含空白或被中途替換，固定此次 public bytes 且失敗不覆寫舊 EXE，由 T5 `RootPublishAndPublicKeySnapshot` 覆蓋。

## Phase、Git 與驗證慣例

只規劃一個 Phase，六個 Task 依序執行，唯一分支建議名 `codex/offline-authorization-phase-5`。核准計畫後才使用 repo-start-work／repo-new-branch 核對最新 remote、PR #10 MERGED、乾淨工作區及名稱衝突，從最新遠端預設分支建立，不另建 worktree。

每個 Task 完成後，記錄 RED／GREEN、差異及限制，按 repo-push 只 stage 當前檔案，commit／push 並確認同名 upstream 0／0，再開始下一 Task。第一個 Task 把核准規格及本計畫正式納入版本控制。Phase 完成後用 repo-open-pr 建唯一 ready PR 並 attach 到目前 chat，不代為 merge 或清理尚未合併的分支。

所有路徑相對於正式專案 Git root。每步確認 exit code，MSBuild 單節點並關閉 server／node reuse。新 suite 在建立前不能以未實作或 skipped 結果通過。

```powershell
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet restore ProgramMigrationAnalyzer.sln
dotnet build ProgramMigrationAnalyzer.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v quiet
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite <當前 suite>
```

## 檔案與介面責任

| Task | 新增或修改的主要位置 | 責任 |
| --- | --- | --- |
| T1 | Infrastructure/Authentication/Authorization*.cs、AuthenticationChecks/AuthorizationSignatureChecks.cs | 嚴格格式、信任公鑰、原始 bytes 驗章 |
| T2 | SignedLocalAccountStore.cs、SignedLocalAuthenticationService.cs、SignedStoreChecks.cs | 有界讀取、revision、鎖定與原子匯入、每次登入驗章 |
| T3 | 新 LicenseIssuer 專案、IssuerChecks.cs | 公司金鑰保護、帳號維護與簽發 WPF |
| T4 | App 啟動、Activation 視窗／VM、UAC launcher、SignedStartupChecks.cs | 首次啟用、更新、合法參數、公司公鑰內建，移除舊管理功能 |
| T5 | publish.bat、publish-issuer.bat、build/ 公鑰準備工具、PublishChecks | 公鑰快照、雙程式分離、原目錄最新腳本 |
| T6 | 完整 suites、README、交付紀錄、正式規格 | 累積回歸、發布與授權部署、審查及唯一 PR |

### Task P5-T1：簽章契約、嚴格 parser 與公鑰 verifier

**Files:** Create `src/ProgramMigrationAnalyzer.Infrastructure/Authentication/AuthorizationModels.cs`、`AuthorizationException.cs`、`AuthorizationTrust.cs`、`AuthorizationPackageCodec.cs`、`SignedAuthorizationVerifier.cs`、`tests/ProgramMigrationAnalyzer.AuthenticationChecks/AuthorizationSignatureChecks.cs`、`AuthorizationTestFixture.cs`。Modify `tests/ProgramMigrationAnalyzer.AuthenticationChecks/Program.cs`、`docs/ProgramMigrationAnalyzer_Codex_Spec.md`。Create 正式核准 spec／plan。

**Interfaces:**

- `AuthorizationPayload(int SchemaVersion, string ProductId, Guid AuthorizationId, int Revision, DateTimeOffset IssuedAtUtc, IReadOnlyList<LocalAccountRecord> Users)`，包含帳號資料的型別覆寫 ToString，不輸出 records。
- `AuthorizationTrust.None`、`AuthorizationTrust.FromPublicKeyPem(ReadOnlySpan<char> pem)`，持有驗證過且複製的 SPKI bytes 與 keyId，無 runtime 外部檔案查找。
- `AuthorizationPackageCodec.EncodePayload(AuthorizationPayload payload): byte[]`、`BuildSigningInput(string keyId, ReadOnlySpan<byte> payload): byte[]`、`EncodeEnvelope(string keyId, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature): byte[]`。
- `SignedAuthorizationVerifier(AuthorizationTrust trust)`、`Verify(ReadOnlyMemory<byte> envelope): VerifiedAuthorization`、`ReadAndVerifyAsync(Stream stream, CancellationToken ct = default): Task<VerifiedAuthorization>`。
- `VerifiedAuthorization` 只讀 `Payload`、`KeyId`、`PayloadFingerprint`，`CopyEnvelopeBytes(): byte[]`、`CopyPayloadBytes(): byte[]` 回傳副本，constructor 非 public，ToString 不含 salt／hash。
- `AuthorizationFailure` 分類 `Missing, InvalidData, InvalidSignature, UntrustedKey, UnsafeAccess, StorageUnavailable, Busy, StaleRevision, ReplacementConfirmationRequired`。`AuthorizationException(AuthorizationFailure failure)` 只保存安全分類。

- [x] 寫 RED 檢查 `ValidCompanySignature`、`EveryProtectedFieldTamperIsRejected`、`WrongKeyAlgorithmProductAndSchema`、`DuplicateUnknownAndTrailingData`、`GrowingStreamIsBounded`、`TrustRejectsPrivatePemAndOtherCurves`。斷言每個篡改不得回傳 VerifiedAuthorization，4 MiB＋1 bytes／深度 17 拒絕，16／32／64 bytes 與 600,000～2,000,000 邊界固定。

```csharp
CheckSupport.Check(verified.Payload.SchemaVersion == 2 && verified.Payload.Revision == 1, "Unexpected signed payload.");
CheckSupport.Throws<AuthorizationException>(() => verifier.Verify(tamperedEnvelope));
CheckSupport.Throws<AuthorizationException>(() => verifier.Verify(unknownKeyEnvelope));
```

- [x] 執行 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite signature`，確認新行為確實 RED，缺少型別造成的 build failure 可記錄但不是 GREEN。
- [x] 在上列檔案實作介面。驗章使用 ECDsa.VerifyData 與明確 P1363。有界 stream 最多讀上限＋1 bytes，UTF-8 使用 throwOnInvalidBytes，嚴格屬性集合檢查。fixture 用記憶體 ECDsa 直接簽測試 payload，不存私鑰。
- [x] 執行 build、signature、contracts、crypto suites，全部 exit 0。確認 verify 與 fixture 沒有把完整 Key／hash 加入錯誤輸出。
- [x] 將核准 spec／plan複製到正式 docs，規格第 37 節新增 signed 模式後續要求與明確實作狀態，既有 Phase 4 證據保留。記錄當前 Task 結果。
- [x] 用 repo-push 提交及推送當前 Task，commit `5a791c6`，確認乾淨與 upstream 0／0。

### Task P5-T2：簽章帳號 store、原子匯入與真實認證

**Files:** Create `src/ProgramMigrationAnalyzer.Infrastructure/Authentication/SignedLocalAccountStore.cs`、`SignedLocalAuthenticationService.cs`、`tests/ProgramMigrationAnalyzer.AuthenticationChecks/SignedStoreChecks.cs`。Modify `AuthorizationModels.cs`、`tests/ProgramMigrationAnalyzer.AuthenticationChecks/Program.cs`、`AuthorizationTestFixture.cs`。

**Interfaces:** Consumes T1 verifier、VerifiedAuthorization、AuthorizationException 及現有 `ILocalAccountAccessPolicy`／LocalPasswordHasher。

- `SignedLocalAccountStore(string filePath, ILocalAccountAccessPolicy policy, SignedAuthorizationVerifier verifier)`，`DefaultFilePath` 與舊路徑相同。
- `ReadAsync(CancellationToken ct = default): Task<VerifiedAuthorization>`。
- `ImportAsync(ReadOnlyMemory<byte> envelope, Guid? confirmedReplacementAuthorizationId = null, CancellationToken ct = default): Task<AuthorizationImportResult>`。確認參數指目前目標 id，僅允許使用者已確認的跨 id 替換，所有輸入仍重新驗章。
- `AuthorizationImportResult(Guid AuthorizationId, int Revision, string PayloadFingerprint, bool Changed)`，安全 ToString。
- `SignedLocalAuthenticationService(SignedLocalAccountStore store, LocalPasswordHasher hasher): IAuthenticationService`，成功 provider 固定 `SignedLocal`。

- [x] 寫 RED 檢查 `ValidEnabledCredentialsRequireSignature`、`UnsignedAndTamperedStoreFailsClosed`、`ReauthenticationReadsAgain`、`TwoDeploymentsUseSameKey`、`RevisionAndIdRules`、`ReplacementConfirmationUsesLockedCurrentId`、`InterruptedImportRollsBack`、`CanceledAndNonElevatedImportPreservesFile`、`UnsafePathsLockAndBusy`。相同 revision 不同 payload 拒絕，跨 id 需正確目前 id，同一 Key 在兩個隔離路徑登入成功。

```csharp
CheckSupport.Check(validLogin.IsSuccess && !tamperedLogin.IsSuccess, "Login must require signed credentials.");
CheckSupport.Check(deploymentOneLogin.IsSuccess && deploymentTwoLogin.IsSuccess, "Key must be portable.");
CheckSupport.Check(File.ReadAllBytes(accountPath).SequenceEqual(originalBytes), "Failed import changed prior data.");
```

- [x] 執行 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite signed-store`，確認 RED。
- [x] 實作上述三個方法。store 必須鎖定後重讀目前授權再決定 revision／確認要求，5 秒 lock 上限，取消可中止等待，同目錄 WriteThrough／flush／原子替換與受保護 rollback。Verifier 必須覆蓋匯入前及替換後檢查，無 unsigned Update callback。
- [x] 認證服務重讀 store 後才依正規化帳號、啟用與 PBKDF2 比對，未知帳號執行 dummy PBKDF2，例外映射現有 AuthenticationFailure，取消照舊往上傳遞。
- [x] 執行 build、signature、signed-store、contracts、crypto、既有 store／access suites，exit 0。ACL 使用隔離 fake policy 的結果與真實 policy 邊界檢查分別記錄，確認所有檔案寫入只在 task temp。
- [x] 更新計畫結果並 repo-push，commit `52fcb92`，確認乾淨與 upstream 0／0。

### Task P5-T3：公司 WPF 發證工具與加密私鑰處理

**Files:** Create `src/ProgramMigrationAnalyzer.LicenseIssuer/ProgramMigrationAnalyzer.LicenseIssuer.csproj`、`App.xaml`／`.cs`、`IssuerWindow.xaml`／`.cs`、`ViewModels/IssuerViewModel.cs`、`Services/IssuerSigningKey.cs`、`IssuerAccountEditor.cs`、`LicenseIssuanceService.cs`、`ProtectedIssuerFileWriter.cs`、`tests/ProgramMigrationAnalyzer.AuthenticationChecks/IssuerChecks.cs`。Modify solution、AuthenticationChecks csproj／Program.cs。App csproj 不引用 LicenseIssuer。

**Interfaces:** Consumes T1 codec／payload／verifier，existing LocalPasswordHasher／LocalAccountRecord。

- `IssuerSigningKey : IDisposable`，`Generate(): IssuerSigningKey`、`ImportEncrypted(ReadOnlySpan<byte> pkcs8, SecureString protectionPassword): IssuerSigningKey`、`ExportEncrypted(SecureString protectionPassword): byte[]`、`ExportPublicPem(): string`、`SignPayload(AuthorizationPayload payload): byte[]`。
- `IssuerAccountEditor.Create(AuthorizationPayload current, string username, string displayName, SecureString password): AuthorizationPayload`、`Reset(...)` 同參數、`SetEnabled(AuthorizationPayload current, string username, bool enabled): AuthorizationPayload`，不在每次編輯增加 revision。
- `IssuerAccountEditor.CreateAccount(string username, string displayName, SecureString password): LocalAccountRecord`，供第一筆帳號及 Create 使用，首次簽發透過 CreateNew 建立含這筆記錄的 payload，不需要持久化空帳號清單。
- `LicenseIssuanceService.CreateNew(IReadOnlyList<LocalAccountRecord> users, DateTimeOffset issuedAtUtc): AuthorizationPayload`、`Reissue(AuthorizationPayload edited, DateTimeOffset issuedAtUtc): AuthorizationPayload`。新 revision 1，重簽增加 1，checked 防溢位。
- `ProtectedIssuerFileWriter.WriteNewPrivateKeyAsync(string path, ReadOnlyMemory<byte> encryptedPkcs8, CancellationToken ct): Task`、`WriteAuthorizationAsync(string path, ReadOnlyMemory<byte> envelope, CancellationToken ct): Task`。私鑰禁止自動覆寫，原子 Key 儲存失敗保留舊檔。

- [x] 寫 RED 檢查 `EncryptedKeyMemoryRoundTrip`、`WrongProtectionPasswordFails`、`IndependentSecretsAndEightCharacterSymbols`、`IssueCreateResetDisableLifecycle`、`RevisionOverflowAndInvalidAccounts`、`IssuerUiClearsSecretsAndRejectsDuplicateSubmit`。測試私鑰匯出／重載只用記憶體，確認 UI 對兩組 PasswordBox 分別清除且不持有可觀察的 password 屬性。

```csharp
CheckSupport.Check(verifier.Verify(reissuedEnvelope).Payload.Revision == 2, "Reissue must increment revision.");
CheckSupport.Check(resetUser.UserId == originalUser.UserId && resetUser.Salt != originalUser.Salt, "Reset must preserve id and refresh salt.");
CheckSupport.Check(!disabledUser.IsEnabled, "Disabled account must remain disabled in the signed package.");
```

- [x] 執行 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite issuer`，確認 RED。
- [x] 實作服務，私鑰 export 使用 PbeParameters 固定 AES-256-CBC／SHA256／600,000。SecureString 轉換的可清除 buffer 在 finally 清除，ECDsa 及密碼副本及時 Dispose，失敗訊息不含 PEM／hash／payload。
- [x] 建 WPF 工具，提供產生金鑰、載入加密私鑰、開啟既有有效 Key、帳號清單與新增／重設／啟用／停用、匯出 Key。確認密碼由 code-behind 送短生命週期 submission，PBKDF2／簽章在背景。私鑰輸出位置需使用者確認，建立時停止 ACL 繼承，只授予目前公司 Windows 使用者及 SYSTEM FullControl，拒絕 reparse path，不存 ProgramData 或 repo。UI 不能無提示覆寫既有私鑰。
- [x] 執行 build、signature、signed-store、issuer、crypto suites，exit 0。STA UI 檢查採 test host 注入記憶體 signing key，不透過正式 App 參數或環境變數造登入捷徑。不產生正式金鑰。
- [ ] 更新計畫結果並 repo-push，建議 commit `feat: add company offline license issuer`，確認乾淨與 upstream 0／0。

### Task P5-T4：公司公鑰內建、首次啟用、UAC 與移除客戶端管理

**Files:** Create App `AuthorizationActivationWindow.xaml`／`.cs`、`ViewModels/AuthorizationActivationViewModel.cs`、`Services/EmbeddedAuthorizationTrust.cs`、`AuthorizationImportLauncher.cs`、`AuthorizationStartupRequest.cs`、`tests/ProgramMigrationAnalyzer.AuthenticationChecks/SignedStartupChecks.cs`。Modify `App.xaml.cs`、`ApplicationSessionCoordinator.cs`、`LoginWindow.xaml`／`.cs`、`LoginViewModel.cs`、App csproj、AuthenticationChecks Program／StartupChecks／AccessChecks、WpfChecks。Delete 客戶端 `LocalAccountConfigurationWindow.xaml`／`.cs`、`LocalAccountConfigurationViewModel.cs`、legacy `LocalAccountAdministrationService.cs`／`LocalAccountStore.cs`／`LocalAuthenticationService.cs`，相應測試移植為 signed 檢查。

**Interfaces:** Consumes T2 store／provider、T1 trust，session 契約不變。

- `AuthorizationStartupRequest.Parse(string[] args): AuthorizationStartupRequest`，只產生 `Normal` 或 `ImportAuthorization(string AbsolutePath)`。
- `EmbeddedAuthorizationTrust.Load(): AuthorizationTrust`，只讀已編譯 resource，缺少返回 None，不接受外部 runtime 公鑰。
- `IAuthorizationImportLauncher.ImportAsync(string absoluteCandidatePath, CancellationToken ct): Task<AuthorizationImportProcessResult>`，結果 `Succeeded, Canceled, Failed`。正式 launcher 使用同一 EXE、UseShellExecute／runas，固定參數形狀與正確 Windows quoting，不傳帳密。
- `AuthorizationActivationViewModel.PreviewAsync(string path, CancellationToken ct): Task`、`ImportAsync(CancellationToken ct): Task`、`Cancel(): void`、`event Action Activated`／`Cancelled`。Preview 只存驗證後安全摘要與 fingerprint，匯入前重讀，子程序後重讀 installed store，核對 fingerprint。
- Coordinator 移除舊 admin factory，接受 signed store 與 activation factory，新增 `InspectingAuthorization`／`Activation`／`ImportingAuthorization` 狀態。正常有效授權才 ShowLogin，Activated 僅返回 Login，Login 成功才建立 Main。

- [ ] 寫 RED 檢查 `MissingAndUnsignedStartOnlyActivation`、`RealSignedProviderStartupAndReauthentication`、`StrictImportStartupModes`、`ImportModeNeverAuthenticates`、`CandidateChangedBeforeElevation`、`CanceledElevationAndLateCompletion`、`EmbeddedTrustCannotBeOverridden`。測試直接走正式 App.OnStartup，真實 signed provider、store、ECDSA 與 PBKDF2，只替換 ACL／UAC launcher。

```csharp
CheckSupport.Check(!coordinator.Session.IsAuthenticated && mainCreationCount == 0, "Activation cannot authenticate.");
CheckSupport.Check(coordinator.State == ApplicationSessionState.Activation, "Missing authorization must enter activation.");
CheckSupport.Throws<ArgumentException>(() => AuthorizationStartupRequest.Parse(["--configure-local-account"]));
```

- [ ] 執行 build 與 `--suite activation`，確認新行為 RED。明確斷言舊 `--configure-local-account` 不再開管理視窗，無主工作區與帳號寫入。
- [ ] 實作上述介面及 WPF 啟用畫面，合法參數逐一判斷。提升模式重讀驗章、顯示實際摘要並確認、要求 elevated token，store 匯入成功 exit 0，使用者取消／錯誤維持未登入。正常模式保留 UAC 取消提示與 generation 檢查，禁止晚到 Activated 開主畫面。
- [ ] App csproj 條件式嵌入公鑰快照為 `ProgramMigrationAnalyzer.AuthorizationPublicKey` resource。缺失公鑰使用 None fail closed，正式組裝不引用 issuer。移除舊管理程式，Windows policy 用到的既有 configuration exception 移至 `LocalAccountConfigurationException.cs`，不保留舊可寫 store。現有 models 移除 obsolete LocalAccountFile，保留 LocalAccountRecord／PasswordHashRecord。
- [ ] 將原 store／admin／local-startup fixtures 與 WpfChecks 遷移到簽章資料，suite registry 保留 contracts／crypto／login／startup／access 並加入 signature／signed-store／issuer／activation，移除失效的 unsigned admin／store 情境。跑全部 AuthenticationChecks、Phase2Checks、RegressionChecks、WpfChecks，exit 0，檢查登入／登出／取消／六個命令及忙碌／晚到結果。
- [ ] 更新計畫結果並 repo-push，建議 commit `feat: activate signed accounts before desktop login`，確認乾淨與 upstream 0／0。

### Task P5-T5：公鑰發布守門、雙工具分離與原目錄 publish 更新

**Files:** Create `build/ProgramMigrationAnalyzer.PublishPreparation/ProgramMigrationAnalyzer.PublishPreparation.csproj`、`Program.cs`、`PublicKeySnapshot.cs`、`publish-issuer.bat`、`tests/ProgramMigrationAnalyzer.AuthenticationChecks/PublishChecks.cs`。Modify `publish.bat`、App csproj、AuthenticationChecks Program.cs、`.gitignore`。所有實作直接在正式根目錄，保留既有未追蹤使用者簡報，不將其混入本 Task 提交。

**Interfaces:** Preparation 是 build 工具，不是客戶端 runtime，引用 T1 public-key parser。

- `PublicKeySnapshot.Prepare(string publicKeyPath, string stagingDirectory): string`，有界讀取且只接受 P-256 SPKI PEM，固定已驗證 bytes，回傳 staging 的 normalized public PEM 路徑。只讀 public bytes，不產生私鑰。
- Prep CLI 只接受 `--public-key <path> --staging <path>`，success 0、輸入或解析錯誤非零，僅安全錯誤與公鑰 fingerprint。
- publish.bat 接受零參數使用建置來源 `config/authorization-public-key.pem`，或唯一 public PEM 路徑參數。直接在正式根目錄建置，正確處理 absolute public path，不再轉送暫存 worktree。App `PrepareAuthorizationPublicKey` target 在 PrepareForBuild 前準備有效 snapshot，再嵌入資源。`RequireAuthorizationPublicKeyForPublish` 在 PrepareForPublish 前拒絕缺失 snapshot 或 NoBuild=true，直接 dotnet publish 也無法省略公鑰守門或以不匹配的預建 EXE 發布。

發布守門安排在建置後的 PrepareForPublish 前，公鑰資源則先準備再編譯。順序已對照 [.NET 10 SDK 的 Microsoft.NET.Publish.targets](https://github.com/dotnet/sdk/blob/v10.0.100/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Publish.targets)，實作時仍須以本機實際 SDK 與發布測試核對。

- [ ] 寫 RED 檢查 `MissingInvalidAndPrivatePemPublishFails`、`RootPublishAndPublicKeySnapshot`、`FailedPublishKeepsPriorExe`、`IssuerAndClientDeliveryAreSeparate`。私鑰 PEM 拒絕測試用無效標記字串，不保存私鑰。snapshot 後修改原 public file，仍編譯預先固定的 bytes，錯誤來源不得回落舊版 unsigned EXE。

```csharp
CheckSupport.Check(missingKeyExitCode != 0 && privatePemExitCode != 0 && noBuildExitCode != 0, "Invalid publish must fail.");
CheckSupport.Check(File.ReadAllBytes(destinationExe).SequenceEqual(priorExeBytes), "Failed publish replaced the prior EXE.");
CheckSupport.Check(embeddedKeyId == snapshotKeyId, "Published trust must use the validated public key snapshot.");
```

- [ ] 執行 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite publish`，確認 RED。沿用前次 publish routing 測試方法，新增腳本測試放 task temp，不提交測試產物。
- [ ] 實作 preparation／MSBuild target，避免 preparation 和 App 相互循環 build。公鑰快照是標準 obj 產物，不在正式 repo 生出暫存憑證。發布先輸出 staging，成功才替換既有客戶 EXE，失敗保留先前 EXE，正確處理空白路徑與 child exit code。
- [ ] 實作獨立 issuer publish 到 `publish/company-license-issuer`，client 仍為 `publish/win-x64-single-file`。保留自包含 Windows x64 單檔、IncludeNativeLibrariesForSelfExtract、PublishTrimmed=false、單節點與 node reuse 關閉。檢查目錄沒有 issuer／實際 Key／私鑰／users.json，更新腳本提示。
- [ ] 執行 publish suite，並使用記憶體測試金鑰匯出的公鑰在隔離 task 目錄實際發佈兩個 EXE。只保存 public PEM，不保存測試私鑰。檢查 client 內嵌 fingerprint 與指定 public 一致、無公鑰直接 publish 失敗，以及正式根目錄建置／輸出的 EXE 指紋一致。此為發佈管線驗證，不替代正式公司金鑰部署。
- [ ] 保留原目錄其餘檔案指紋與狀態，直接在正式根目錄維護及驗證 publish.bat。記錄結果並 repo-push，建議 commit `fix: publish signed authorization client and issuer separately`，確認乾淨與 upstream 0／0。

### Task P5-T6：完整回歸、公司金鑰交接、授權部署與 ready PR

**Files:** Modify `README.md`、`docs/ProgramMigrationAnalyzer_Codex_Spec.md`、正式本計畫。Create `docs/Offline_Authorization_Verification_and_Delivery.md`。必要回歸修正限於本 Phase 程式與測試檔案，仍記錄 RED／GREEN。

**Interfaces:** 使用已完成的公司 issuer、客戶 EXE、publish 腳本及所有 suite，不引入新功能或正式程式測試 bypass。

- [ ] 執行 restore、build、AuthenticationChecks all、Phase2Checks、RegressionChecks、WpfChecks，確認全部 exit 0、build 0 warnings／0 errors，列出實際 checks 數量。比對 O1～O12 與本計畫 Review Focus 的覆蓋，不將新版檢查數套用舊版 56 組。
- [ ] 完成全 Phase 自審，檢查 App 不引用 issuer、內建資源只有公鑰、無舊 admin store／模式／runtime trust override、無 secrets／實際 Key／temp／publish 產物被 tracked。官方驗證與測試 fixture 不共用會掩蓋錯誤的「假驗章」服務。
- [ ] 開啟已發布的公司發證工具，請使用者互動產生並保存正式加密私鑰、提供其公開公鑰檔路徑，簽發實際測試帳號 Key。代理只核對 public fingerprint，不讀取私鑰或詢問密碼。私鑰目的地在工作區外時，使用者選定及授權該目的地後才執行相關操作。
- [ ] 在替換真實帳號檔前，取得指定測試機的新版本 ProgramData／ACL 授權，具體說明舊 unsigned 帳號將換成公司 Key、建議的恢復方式與可保留備份。授權未取得可完成其餘隔離工作，但本 Task 的真實部署保持未完成。
- [ ] 用正式公鑰 publish，使用者手動完成 UAC／Key 匯入／帳密登入。實測取消、首次啟用、舊入口拒絕、不同工作目錄、重啟、Scenario A／B／C、分析／轉譯、HTML／純文字、忙碌登出、重新登入與輸出保留、關閉無殘留程序。拒絕測試只用隔離資料，不破壞真實 Key。核對真實 ACL 與祖先 ACL 不變，不打印 hash／salt。
- [ ] 更新 README 的公司 keygen／issue／import／update／publish、公鑰設定、8 字元規則、舊版遷移、私鑰備份、公鑰更換、離線重放及主機管理員限制。交付紀錄分列自動化、publish、真實部署、未執行項目與必要恢復手續。
- [ ] 經驗證後用 repo-push 提交及推送，建議 commit `test: verify offline authorization and document delivery`，確認工作區乾淨、upstream 0／0。所有必要驗收完成才用 repo-open-pr 建立唯一 ready PR，描述正式客戶端行為、測試及限制並 attach 到目前 chat，不自動 merge。

## 驗收對照與進度

| 規格 | 實作與自動化 | 真實部署 |
| --- | --- | --- |
| O1、O2、O7 | T2 provider、T4 activation／startup | T6 |
| O3、O4、O5 | T1 signature／parser、T2 bounded store | T6 僅必要正式流程觀察 |
| O6 | T2 atomic／ACL、T4 UAC／取消 | T6 |
| O8、O9 | T3 issuer／密碼與記憶體金鑰 | T6 使用者操作正式 issuer |
| O10 | T2 revision／雙隔離部署 | 不額外要求第二台真實主機 |
| O11 | T5 publish／snapshot／轉送 | T6 正式 public key publish |
| O12 | T4 累積回歸、T6 完整 suites | T6 桌面／樣本／退出 |

P5-T1 已完成並推送 5a791c6。P5-T2 已完成並推送 52fcb92。P5-T3 已完成程式與驗證，待提交／推送，P5-T4～T6 尚未開始。使用者已核准按本計畫開分支、逐 Task 驗證／commit／push，Phase 完成建立唯一 PR。分支只在實際開始當前 Phase 時建立，不預建後續 Phase。授權範圍不含 merge、force push、歷史覆寫或未指定電腦部署。

### P5-T1 執行紀錄

從最新 main 的 `7513324` 建立唯一當前 Phase 分支，GitHub PR #10 已重新查證 MERGED。先寫測試及介面，八個行為檢查實際因未實作而失敗，再實作 ECDSA verifier、嚴格 payload／envelope parser、公鑰信任與有界 stream。另以失敗測試揭露 serializer 會替換無效 UTF-16，修正為序列化前拒絕，共九組 signature 檢查通過。

最終 restore／build、AuthenticationChecks all、Phase2Checks、RegressionChecks、WpfChecks 全部 exit 0。Build 0 warnings／0 errors，Authentication 65 PASS，其中 signature 9 組。受限 restore 的 NU1900 以允許環境強制更新中繼資料後排除，未關閉 NuGet audit。原 store 五個原子替換情境在 sandbox 出現 StorageUnavailable，同一組隔離測試於允許環境全部通過，並未修改真實 ProgramData 或 ACL。

證據保留在忽略的 `.codex-tmp/2026-10-01_offline-authorization-phase-5/`，測試私鑰僅在記憶體，沒有簽發正式 Key。客戶端 composition 尚未切換，P5-T4 及真實部署未開始。

### P5-T2 執行紀錄

2026-10-01：先確認 9 組 NotImplemented RED，再實作真實 signed store／provider。新增 ACL Missing 語意檢查，確認 RED 後修正首次建檔流程，signed-store 共 10 組 GREEN。全部 AuthenticationChecks 75 PASS，build 0 warnings／0 errors。覆蓋 revision／跨 id 確認、兩個隔離部署、鎖內重讀、取消、5 秒 Busy、失敗及鎖內 rollback。測試均限 task temp，fake policy 檢查不取代 T6 真實 ACL 部署。正式客戶端仍待 T4 接入。
### P5-T3 執行紀錄

2026-10-01：公司 LicenseIssuer WPF 已完成。最初服務 5 組 NotImplemented RED 後 GREEN，WPF 欄位／安全 writer 3 組 RED 後 GREEN。補充私鑰目的地、共享鎖定與非法操作模式，觀察非法操作會停用帳號的 RED 後修正，issuer 最終 12 PASS。全部 AuthenticationChecks 87 PASS，build 0 warnings／0 errors。真實 WPF 子程序確認兩組密碼清除、8 字元符號密碼、重複提交、停用與關閉取消。記憶體加密私鑰 roundtrip、Unicode／空白保護密碼與獨立登入密碼、簽發／重設／停用／啟用及 signed provider 的真實登入均通過。

加密私鑰只在記憶體做匯出／重載測試，沒有產生正式金鑰或保存測試私鑰。儲存私鑰時使用 CreateNew 與 Windows 目前使用者／SYSTEM 的保護 ACL，拒絕 Git 專案與 reparse path。授權輸出原子替換，拒絕私鑰目的地，取消與共享衝突保留原檔。私鑰實際儲存及正式 UAC／部署仍待 T6 使用者授權與操作。

作業中依使用者授權完成全部登入成果回到正式根目錄，13 個 T3 檔案、原簡報及 573 個歷史證據逐檔核對，見 [工作區復原](../../Workspace_Restoration_2026-10-01.md)。AGENTS 已明定正式根目錄作業，publish.bat 不再 fallback 暫存 worktree。P5-T5 依最新使用者指示改驗正式根目錄直接發布，簽章發布守門仍待 T5。客戶端接入仍待 T4。
## 計畫自審

2026-10-01 計畫撰寫時：已依規格逐節對照六個 Task 與 O1～O12。確認 T1 型別／方法供 T2～T5 使用一致，沒有未定義的跨 Task function。五項 Review Focus 均有具名檢查，部署與正式私鑰操作保留使用者授權界線。撰寫時所有 checkbox 未勾選，沒有將計畫中的命令或預期結果記成已執行，後續完成項目以 Task 紀錄為準。

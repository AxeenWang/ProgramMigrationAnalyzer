# 本機帳號登入 Phase／Task Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. 本計畫建議由同一位實作者依序執行，只有另獲明確授權時才使用代理分工。

**Goal:** 以本機帳號與密碼驗證使用者，登入成功才建立分析主畫面，登出後必須重新驗證。

**Architecture:** 保留現有 WPF／MVVM 及六個產品專案。Core 定義認證契約與唯讀登入狀態，Infrastructure 實作本機帳號、PBKDF2 與 Windows ACL，App 協調登入視窗、主視窗及獨立帳號設定模式。分析與轉譯元件維持原有責任，正式認證日後透過替換 adapter 接入。

**Tech Stack:** C#、.NET 10、WPF、CommunityToolkit.Mvvm、System.Text.Json、System.Security.Cryptography、Windows 檔案 ACL，測試沿用本專案的 console checks 方式。

**Spec:** [ProgramMigrationAnalyzer_Codex_Spec.md](../../ProgramMigrationAnalyzer_Codex_Spec.md)，第 37 節及第 7、19、22、24～27、32～34 節相關更新。

**日期／狀態:** 2026-10-01／Phase 1 已合併並清理分支，Phase 2 PR #7 已合併並清理兩端分支。Phase 3 已從最新 origin/main 開工，P3-T1～P3-T3 實作與 Task 驗證完成，登入門檻、操作保護及登出已接入，Phase 審查及唯一 PR 尚待完成。Phase 4 尚未開始，發佈驗收尚未完成。原專案範圍為 `D:/AxeenWorld/CodeLab/Projects/ProgramMigrationAnalyzer`，檔案路徑均相對於各 Phase 的 Git root，Phase 1 隔離路徑見開工決議。

## Phase 與分支總覽

**執行規則：只有開始實作該 Phase 時才建立其分支，禁止在規劃或開工時一次建立所有 Phase 分支。一個 Phase 一個分支，依序執行 Task，每完成一個 Task 都須驗證、提交並推送一次，全部 Task 完成後，該 Phase 建立一個 PR。** 不按 Task 建立額外分支或 PR，也不能只完成本機實作就宣稱該 Phase 已交付。

| Phase | 預定分支名稱 | Task | Phase 交付物 | PR 建議標題 |
| --- | --- | --- | --- | --- |
| 1：認證基礎 | `codex/login-phase-1-foundation` | P1-T1、P1-T2 | 認證契約、唯讀 session、測試宿主、密碼規則與雜湊 | `feat: add login contracts and password hashing` |
| 2：本機帳號管理 | `codex/login-phase-2-local-accounts` | P2-T1、P2-T2 | 帳號儲存、ACL、實際驗證、管理服務與設定視窗 | `feat: add local account authentication and administration` |
| 3：桌面登入整合 | `codex/login-phase-3-desktop-gate` | P3-T1、P3-T2、P3-T3 | 登入 UI、正式啟動門檻、命令保護、登出與測試調整 | `feat: require login before opening the analyzer` |
| 4：驗證與交付 | `codex/login-phase-4-verification` | P4-T1、P4-T2、P4-T3 | 完整回歸、發佈 EXE 驗證、README 與交付紀錄 | `test: verify login workflows and document deployment` |

表中的名稱僅供規劃，不是預先建立分支的指示。尚未開始的 Phase 保持「分支未建立」，前一 Phase 合併或清理後也不自動建立下一 Phase 分支。只有開始實作當前 Phase 時，才檢查名稱衝突並建立該分支，實際狀態見 Phase／PR 追蹤紀錄。基底使用實際遠端預設分支，目前本機 `origin/HEAD` 指向 `origin/main`，開工時仍須 fetch 並重新確認。

**串行依賴：** Phase 1 PR 合併 → Phase 2 開分支 → Phase 2 PR 合併 → Phase 3 開分支 → Phase 3 PR 合併 → Phase 4 開分支。採用依序合併方式，不使用從前一個未合併主題分支開出的堆疊分支。

Phase 1／2 交付可測試的基礎元件，尚未替換正式啟動流程，不代表產品已完成登入。Phase 3 必須在同一 PR 內同時完成登入門檻、操作入口與登出，避免合併只有畫面、沒有操作保護的半套登入。Phase 4 完成驗證與交付，最終登入版本須以 Phase 4 的驗證結果為準。

## 每個 Phase 的 Git／PR 流程

開始某個 Phase 即按本計畫執行該 Phase 的開分支、實作、驗證、提交、推送及 PR，正常且符合守門條件的步驟不需反覆詢問。此授權不包含 merge、force push、改寫歷史、刪除分支或部署到未授權電腦。開工授權與實際進度見本計畫的開工決議及 Phase／PR 追蹤紀錄。

1. **開始：** 只在開始實作當前 Phase 時開分支，確認 Active Target 等於 Git root，讀取適用規範，確認 worktree／index 完全乾淨，前一 Phase PR 已合併，並取得最新遠端預設分支，使用 `repo-new-branch` 建立該 Phase 的專屬分支，初始不追蹤預設分支。不得同時建立後續 Phase 分支。若該 Phase 已開工，確認後續工作仍在原 Phase 分支，不另建分支或覆寫同名分支。
2. **實作 Task：** 只完成該 Phase 的當前 Task，保留測試先行步驟、必要修正及進度紀錄。不要提前加入下一 Task 或 Phase 的功能。
3. **每 Task 驗證與推送：** 每完成一個 Task，先通過該 Task 的檢查及受影響回歸，再依 `repo-push` stage／commit／正常 push 一次，確認 HEAD 與同名 upstream 同步後，才開始下一 Task。首次 push 建立同名 upstream，後續 push 使用原 upstream。只能 stage 當前 Task 與必要文件，不能納入既有截圖、簡報、帳號資料或其他工作，本範圍限制優先於 skill 的廣泛 `git add -A`。
4. **Phase 驗證：** 所有 Task 各自完成並推送後，執行該 Phase 的檢查與累積回歸，審查完整差異、機密、temp、產物與無關內容。若驗證或審查需要修正，在同一分支完成並推送修正，再重跑受影響檢查，不跳過任何 Task 的推送紀錄。
5. **建立 PR：** worktree 乾淨、分支已推送、upstream 指向同名 `origin/<Phase 分支>`，ahead／behind 皆為 0，才使用 `repo-open-pr` 建立 ready PR，base 為遠端預設分支。同一 Phase 已有 open PR 時沿用該 PR，不重複建立。
6. **確認與回報：** 查證 PR 的 head／base／URL 與狀態，附加 PR 至目前 Codex chat，回報已完成 Task、檢查結果、commit、分支、upstream、剩餘變更及 PR URL。
7. **下一 Phase：** PR 建立後本 Phase 狀態為「已送審」，由使用者或既有審查流程完成合併。確認 merged 後才具備下一 Phase 的開工條件，不以「PR 已建立」代替「PR 已合併」。合併後先完成清理，直到實際開始下一 Phase 時才建立其分支。

PR 內容至少列出本 Phase 目標、完成的 Task ID、實際驗證結果、使用行為、限制及後續 Phase，不把下一 Phase 才完成的功能寫成已完成。審查修正繼續使用同一 Phase 分支與 PR。

若必要測試失敗、worktree 不乾淨、遠端領先／分歧、upstream 不明、分支名稱衝突、GitHub 權限不足或無法建立 PR，停止相應 Git 操作並保留內容，Phase 仍為未交付，回報具體原因與下一步，不改用 force 或偷偷略過 PR。一般測試失敗應先修正後重跑。

## 正式開工前的準備條件

目前工作區有本次登入規格與計畫文件，以及既有未追蹤截圖／簡報，且目前分支沒有 upstream。這不阻擋文件規劃，但不符合 `repo-new-branch` 的乾淨工作區要求。

正式開始 Phase 1 前，先由使用者決定既有工作的保存／收尾方式，並讓登入規格與計畫成為可追溯的開發基線，再確認工作區乾淨，不得自動 stash、攜帶變更切分支、提交無關內容、改 ignore 規則掩蓋資料或刪除檔案。這是開工前置條件，不另計為實作 Phase。

2026-10-01 開工決議：使用者同意在專案內 `.codex-tmp/2026-10-01_login-phase-1/worktree` 建立隔離 worktree，以該目錄作為 Phase 1 Git root，原工作區內容保留，登入規格與本計畫帶入 P1-T1 作為基線文件。隔離 worktree 完成乾淨狀態檢查後可開始，不要求清理原工作區。

此隔離 checkout 的 optional bootstrap 無法透過相對路徑確認 CodeLab anchor，依專案規則採 standalone 模式，使用者指定的原專案範圍與隔離限制仍適用。僅將原工作區的登入規格與計畫作為已授權的外部文件，不擴張至其他目錄。

## Global Constraints

- 原專案 `Projects/ProgramMigrationAnalyzer` 使用 CodeLab-managed 模式，Target Lock 啟用。Phase 1 隔離 Git root 採用開工決議所載的 standalone 模式，適用該 checkout 的 `AGENTS.md`，不讀取兄弟專案。
- App Target Framework Moniker：`net10.0-windows`，Core／Infrastructure 維持現有 `net10.0`，Windows API 集中於有平台標註的 adapter。
- 「先使用本機帳號與密碼，之後再接正式認證。」本階段只選擇 Local provider，不實作正式 API／Entra adapter。
- 帳號檔固定為 `%ProgramData%/ProgramMigrationAnalyzer/auth/users.json`，`schemaVersion` 為 1，正常登入不能寫入帳號資料。
- `passwordAlgorithm` 為 `PBKDF2-HMAC-SHA256`，新密碼使用 600,000 次 iterations、至少 16 bytes 新 salt、32 bytes 雜湊，驗證接受 600,000～2,000,000 次 iterations。
- 帳號 3～64 個 ASCII 字元，只允許英文字母、數字、點、底線、連字號，Trim 後 invariant 小寫正規化。密碼不 Trim、不轉大小寫、不截斷，新建／重設為 15～128 個 Unicode scalar values。
- 「連續 5 次失敗後，此程序的登入提交暫停 30 秒」，節流只存在程序內，不提供跨程序永久鎖定。
- 管理模式為 `--configure-local-account`，必須以已提升權限的 Windows 管理者程序執行，不自動提升，不以工具帳號登入代替 Windows 管理權限。
- 不提供自助註冊、共用預設密碼、記住密碼、自動登入、角色矩陣或帳號同步。session 只存在程序記憶體，沒有 idle timeout。
- 帳號停用／重設於下次登入生效，不承諾即時撤銷既有 session。登出清除記憶體工作區，保留 output 檔案。
- 不安裝新套件、不改全域設定，若既有 SDK reference 無法提供必要 API，先查明最小依賴並依既有授權範圍處理，不藉此升級其他套件。
- 測試帳號及產物只放在 `.codex-tmp/YYYY-MM-DD_<task-name>/`，自動化不碰真實 ProgramData。外部部署與 ACL 寫入需取得該電腦的部署授權。
- 實作前重新確認 Git root、分支、upstream 及工作區，目前分支 `codex/operation-guide-slides` 沒有 upstream，且有規格變更與既有未追蹤截圖／簡報。不得自行切換、stash、清理、提交或推送這些內容。
- 每個 Task 記錄驗證結果及差異，完成後立即 commit／push，每個 Phase 在所有 Task 完成後建立唯一 PR，agent temp 不可提交。

## Review Focus

1. 取消後晚到的成功結果：不得開啟主畫面或建立 session，由 P3-T1、P3-T2 驗證。
2. 重複帳號、無效 Base64、極端 iterations、Unicode 密碼：拒絕損壞設定，密碼不改寫，由 P1-T2、P2-T1 驗證。
3. 檔案 ACL／父目錄允許一般使用者替換資料，或更新中斷：不得信任可被替換的帳號檔，也不得破壞舊資料，由 P2-T1、P2-T2 驗證。
4. 檔案對話框返回時已登出、直接呼叫命令、非同步結果晚到：不得繼續讀寫或更新舊工作區，由 P3-T3 驗證。
5. 不同工作目錄、單檔 EXE 與既有測試直接建立主視窗：正常入口都必須登入，測試替身只能由測試組裝注入，由 P3-T2、P4-T2 驗證。

## 現況與檔案責任

目前 `App.OnStartup` 直接建立 `MainViewModel` 及 `MainWindow`，並以 `MainWindow is not null` 提早返回。`MainViewModel` 的操作條件只有選取文件及忙碌狀態，`OpenOutputFolder` 會直接建立 output。`WpfChecks` 直接呼叫 `app.Run(window)`，`Phase2Checks` 亦建立 MainViewModel，均需在登入整合時調整。

| 位置 | 新增檔案／責任 | 既有檔案變更 |
| --- | --- | --- |
| `src/ProgramMigrationAnalyzer.Core/Authentication/` | `AuthenticationContracts.cs`、`AuthenticationModels.cs`：認證與唯讀 session 契約 | 不把登入混入現有 `Models.cs`／parser 契約 |
| `src/ProgramMigrationAnalyzer.Infrastructure/Authentication/` | `LocalAccountModels.cs`、`LocalAccountValidation.cs`、`LocalPasswordHasher.cs`、`LocalAccountAccessPolicy.cs`、`LocalAccountStore.cs`、`LocalAuthenticationService.cs`、`LocalAccountAdministrationService.cs` | 必要時調整 Infrastructure csproj 的平台警告處理，不改原有服務 |
| `src/ProgramMigrationAnalyzer.App/` | `LoginWindow.xaml/.cs`、`LocalAccountConfigurationWindow.xaml/.cs` | `App.xaml`、`App.xaml.cs`、`MainWindow.xaml/.cs`、`AssemblyInfo.cs`（測試可見性） |
| `src/ProgramMigrationAnalyzer.App/Services/` | `AuthenticatedSession.cs`、`ApplicationSessionCoordinator.cs`、`MainWindowFactory.cs`、`AuthenticationDiagnosticLog.cs` | composition root 保持 constructor injection |
| `src/ProgramMigrationAnalyzer.App/ViewModels/` | `LoginViewModel.cs`、`LocalAccountConfigurationViewModel.cs` | `MainViewModel.cs` |
| `tests/ProgramMigrationAnalyzer.AuthenticationChecks/` | csproj、`Program.cs`、`CheckSupport.cs` 與各任務具名 `*Checks.cs` | 加入 `ProgramMigrationAnalyzer.sln` |
| 既有測試／交付 | `docs/Login_Verification_and_Delivery.md`（實作完成時才建立） | Phase2Checks、WpfChecks 的 `Program.cs`、`README.md`，`publish.bat` 原則上保留 |

## 執行與驗證慣例

Phase 內依 Task ID 順序執行，Phase 之間依合併順序開始。每項先寫有意義的失敗檢查，再實作、重跑、記錄結果、commit／push，確認推送成功才轉入下一 Task。測試名稱與斷言是行為要求，不是已存在的測試函式。

新增 `AuthenticationChecks` 為 `net10.0-windows`、`UseWPF=true`、`OutputType=Exe`，reference App 與 Infrastructure，不引入測試框架。runner 支援 `--suite contracts|crypto|store|admin|login|startup|access|all`，任何斷言失敗回傳非零，成功輸出 `Authentication <suite> checks passed.`。需要 WPF Application 的案例分別以 STA 子程序執行，不能在同一程序重建多個 Application。runner／替身不打包進產品 EXE。

後續命令全部從 Active Target 根目錄執行。任務內的 `dotnet run` 可建置其測試與 references，整體建置使用專案既有單節點設定。測試暫存目錄及 fake services 透過 constructor injection 提供，不新增環境變數、`--skip-login` 或可供正式 EXE 使用的替身開關。

---

## Phase 1：認證基礎

**Branch:** `codex/login-phase-1-foundation`。**開始條件:** 前置工作已處理，乾淨工作區，最新遠端預設分支。**交付界線:** 認證契約及密碼處理可測試，正式啟動流程尚不變更。

### Task P1-T1：認證契約、登入狀態與檢查宿主

**進度:** contracts 5 項及既有三組回歸通過，Build 0 warning／0 error。已提交並推送 `55c5b17`，確認 upstream ahead／behind 為 0／0 後才開始 P1-T2。

**Files:** Create Core 的兩個 Authentication 檔案、App 的 `Services/AuthenticatedSession.cs`，以及測試 csproj、`Program.cs`、`CheckSupport.cs`、`ContractChecks.cs`，Modify Solution 與 App 的 `AssemblyInfo.cs`。

**Interfaces:**

- `IAuthenticationService.AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default) -> Task<AuthenticationResult>`。
- `LoginRequestKind` 為 `Credentials`／`Interactive`，`LoginRequest(LoginRequestKind kind, string? username, SecureString? password)` 為可 Dispose 的 class，含 Kind、Username 及唯讀 SecureString 複本，禁止 record 自動印出 credential，ToString 僅回傳類型名稱。
- `AuthenticatedUser(Guid UserId, string Username, string DisplayName, string Provider)`，第一階段 Provider 為 `Local`。
- `AuthenticationResult` 只透過 `Succeeded(AuthenticatedUser)`／`Failed(AuthenticationFailure)` 建立，`IsSuccess` 由 User 是否存在推導，AuthenticationFailure enum 為 InvalidCredentials、ConfigurationInvalid、UnsupportedRequest、UnexpectedFailure。
- `IUserSession`：`bool IsAuthenticated`、`AuthenticatedUser? CurrentUser`、`long Generation`、`event EventHandler? Changed`，全部唯讀。
- App 的 `AuthenticatedSession` 為 internal class，`SetAuthenticated(AuthenticatedUser)`／`Clear()` 只由協調器使用，兩者增加 Generation 並通知 Changed。ViewModel 僅接收 IUserSession。

- [x] 寫 `SessionStartsSignedOut`：初始 CurrentUser 為 null、IsAuthenticated 為 false，`SessionGenerationAndClear`：登入／清除增加 Generation 且發出 Changed，清除後 CurrentUser 為 null，寫 `ResultCannotSucceedWithoutUser`：空使用者不能形成成功結果。
- [x] 寫 `CredentialHasRedactedToStringAndOwnedCopy`：原 SecureString 被釋放不影響請求複本，請求 Dispose 後不能再取得密碼，ToString 不含密碼內容。
- [x] 執行 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite contracts`，確認因缺少契約／實作而失敗，不接受無關 restore 或環境錯誤作為失敗證據。
- [x] 建立以上契約、internal session 與具名檢查宿主，AssemblyInfo 僅對 `ProgramMigrationAnalyzer.AuthenticationChecks` 加入 InternalsVisibleTo，測試實際 internal session。其他操作測試使用各自的 `FakeUserSession : IUserSession`，不新增產品的公開「登入成功」setter。
- [x] 重跑 contracts，所有斷言通過、exit code 0，記錄結果與新增檔案。

### Task P1-T2：帳號驗證規則與密碼雜湊

**進度:** crypto 7 項及累積回歸通過，Build 0 warning／0 error。已提交並推送 `e008a54`，確認 upstream ahead／behind 為 0／0，接著完成 Phase 審查及 PR。

**Files:** Create Infrastructure 的 `LocalAccountModels.cs`、`LocalAccountValidation.cs`、`LocalPasswordHasher.cs`，Create `CryptoChecks.cs`。

**Interfaces:**

- `LocalAccountFile` 含 SchemaVersion、Users，`LocalAccountRecord` 對應規格的 userId、username、displayName、isEnabled 及雜湊欄位。
- `PasswordHashRecord(string Algorithm, int Iterations, byte[] Salt, byte[] Hash)`。
- `LocalAccountValidation.NormalizeUsername(string value) -> string`，`ValidateNewPassword(SecureString password) -> void`，不合法時拋出有安全訊息的 validation exception。
- `LocalPasswordHasher.Create(SecureString password) -> PasswordHashRecord`，`Verify(SecureString password, PasswordHashRecord record) -> bool`。呼叫端負責背景執行。

- [x] 寫 `UsernameCanonicalization`：`" User.One "` → `"user.one"`，拒絕長度 2／65、非 ASCII、斜線，寫 `PasswordUnicodeAndWhitespace`：15／128 scalar 可建帳，14／129 不可，前後空白、大小寫及 Unicode 不被改寫。
- [x] 寫 `SaltAndResetSemantics`：同密碼兩次建立的 salt／hash 不同，正確密碼通過、錯誤密碼失敗，新 hash 不接受舊密碼。
- [x] 寫 `HashParametersRejectedBeforeExpensiveWork`：拒絕未知演算法、iterations 599,999／2,000,001、salt < 16 bytes、hash 非 32 bytes，新記錄固定為 SHA-256、600,000、32 bytes。
- [x] 執行 crypto suite，確認上述行為尚未實作而失敗。
- [x] 實作隨機 salt、PBKDF2、FixedTimeEquals，密碼轉 UTF-8 時保留完整輸入，拒絕無效 surrogate，finally 清除可清除 buffer。不要把 password／hash 放入例外訊息。
- [x] 重跑 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite crypto`，取得成功輸出與 exit code 0。

### Phase 1 驗證與 PR

- [x] P1-T1、P1-T2 全部步驟完成，Solution build 與 contracts／crypto suite 通過，既有 Phase2Checks／RegressionChecks／WpfChecks 回歸通過。
- [x] PR 差異只含 Phase 1 契約、session、雜湊、規則、測試宿主與必要文件，沒有未完成功能的假驗證。
- [x] commit／push 完成，worktree 乾淨，upstream 同步，建立並確認此分支的唯一 PR，記錄 URL。
- [x] 前述 PR 已確認合併，Phase 2 開工前置條件已滿足，尚未開始 Phase 2 或建立其分支。

## Phase 2：本機帳號管理

**Branch:** `codex/login-phase-2-local-accounts`。**開始條件:** Phase 1 PR 已合併，從更新後的遠端預設分支開分支。**交付界線:** 本機帳號與管理元件可測試，`--configure-local-account` 的正式入口由 Phase 3 接入，此 Phase 不宣稱 EXE 已具備完整登入。

### Task P2-T1：帳號儲存、ACL 與實際認證

**進度:** store 8／8 行為檢查從失敗轉為通過，涵蓋檔案驗證、唯讀登入、權限拒絕、原子更新、取消與 5 秒競爭鎖。Solution build 0 warnings／0 errors，累積回歸通過。Windows ACL adapter 尚未做真實部署整合驗證，測試使用 fake policy。隔離檔案原子替換在沙箱內遭 0x80070005 拒絕，相同測試經核准執行通過。此 Task 驗證後提交及推送，確認同步才開始 P2-T2。

**Files:** Create `LocalAccountAccessPolicy.cs`、`LocalAccountStore.cs`、`LocalAuthenticationService.cs`，Create `StoreChecks.cs`。

**Interfaces:**

- `ILocalAccountAccessPolicy`：`bool IsElevatedAdministrator`、`ValidateReadAccess(string filePath)`、`PrepareWriteAccess(string directoryPath)`、`SecureFile(string filePath)`。同檔案內 Windows 實作以 `[SupportedOSPlatform("windows")]` 標註，測試使用 fake。
- `LocalAccountStore(string filePath, ILocalAccountAccessPolicy accessPolicy)`，`ReadAsync(CancellationToken) -> Task<LocalAccountFile>`，`UpdateAsync(Func<LocalAccountFile, LocalAccountFile> update, CancellationToken) -> Task`。
- `LocalAccountConfigurationException`：只攜帶安全分類，不回傳原始 JSON。
- `LocalAuthenticationService(LocalAccountStore store, LocalPasswordHasher hasher)` 實作 P1-T1 的認證介面，正常產品路徑以 CommonApplicationData 取得，僅測試 DI 可以指定隔離檔案。

- [x] 寫 `ValidEnabledAccountOnly`：正確帳密＋啟用才成功，未知、錯誤、停用都回 InvalidCredentials，Interactive 回 UnsupportedRequest，每次驗證重新讀檔。
- [x] 寫 `InvalidAccountFileFailsClosed`：缺檔、半份 JSON、schema != 1、重複正規化帳號／GUID、無效 Base64、錯誤欄位類型回 ConfigurationInvalid，正常驗證不建立或修改檔案。
- [x] 寫 `AclAndInterruptedWrite`：一般使用者具有 Write／Delete／ChangePermissions 或父目錄可替換檔案時拒絕，寫入中斷仍可讀舊記錄，更新成功保留必要 ACL。ACL 斷言以 fake 模擬，真實 adapter 的整合測試只用已授權隔離目錄。
- [x] 執行 store suite，確認上述情境失敗後再實作。
- [x] 實作 camelCase JSON、完整驗證及背景雜湊，未知／停用帳號亦完成等價 dummy hash 工作，再回統一失敗，減少帳號探測的時間差。
- [x] 實作受保護 auth 目錄及檔案 ACL：Administrators／SYSTEM FullControl、Users 讀取，移除不安全繼承寫入規則，不使用會連管理者一併阻擋的 Everyone deny。拒絕會跳出指定 auth 目錄的 reparse points。
- [x] UpdateAsync 只允許提升的管理程序：在受保護目錄取得獨占 lock file 後重讀最新資料，以同目錄安全暫存檔原子替換，取消／失敗保留舊檔並釋放 lock。競爭鎖等待最多 5 秒，逾時提示稍後重試，避免兩個管理程序覆蓋彼此更新。首次無檔案的空 schema 1 初始化僅限此管理路徑，損壞現有檔不可默默覆寫。
- [x] 重跑 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite store`，確認通過，並檢查診斷輸出未包含帳號檔或 hash。

### Task P2-T2：管理者帳號設定模式

**進度:** admin 6／6 行為檢查從失敗轉為通過，包含權限、生命週期、密碼確認、四種操作模式、取消及 STA 視窗隔離。重設保留 GUID 與停用狀態，關閉期間不提交晚到結果，密碼於送出、切換模式及關閉時清除。設定視窗影像已檢視。Solution build 0 warnings／0 errors，contracts 5、crypto 7、store 8、admin 6 及既有三組回歸通過。正式啟動入口尚未接入。驗證後逐 Task 提交、推送，再進行 Phase 審查。

**Files:** Create `LocalAccountAdministrationService.cs`、`LocalAccountConfigurationWindow.xaml/.cs`、`ViewModels/LocalAccountConfigurationViewModel.cs`，Create `AdministrationChecks.cs`。

**Interfaces:**

- `LocalAccountAdministrationService(LocalAccountStore store, LocalPasswordHasher hasher, ILocalAccountAccessPolicy policy)`。
- `CreateOrResetAsync(string username, string displayName, SecureString password, CancellationToken) -> Task`，`SetEnabledAsync(string username, bool isEnabled, CancellationToken) -> Task`。
- `LocalAccountConfigurationViewModel(LocalAccountAdministrationService administration)` 提供帳號、顯示名稱、模式（建立／重設／啟用／停用）、狀態與提交命令，視窗送出兩份短期 SecureString，不將密碼設為可觀察屬性。

- [x] 寫 `NonElevatedCannotManage`：fake policy 非提升時拒絕，沒有檔案寫入、沒有 session，`ManageAccountLifecycle`：建帳、重設保留 GUID、停用／啟用，錯誤時保留既有資料。
- [x] 寫 `ConfirmationAndModeIsolation`：兩密碼不一致不送出，僅切換啟用狀態不需要密碼，管理操作／關閉不顯示分析主視窗。
- [x] 執行 admin suite，確認服務及畫面缺少對應行為而失敗。
- [x] 實作建帳時以新 GUID、isEnabled=true 建立，重設時保留 GUID 與原 isEnabled，避免重設意外重新啟用停用帳號。未知帳號的啟用／停用回安全錯誤，不能隱式建帳。
- [x] 實作小型管理視窗、背景計算及清理，只接受互動遮罩密碼。啟動參數只允許選管理模式，不接受帳密，真正提升與否由 Windows token 檢查，不由工具 session 決定。
- [x] 重跑 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite admin`。實際接入啟動參數由 P3-T2 完成。

### Phase 2 驗證與 PR

- [x] P2-T1、P2-T2 全部步驟完成，Solution build 與 contracts／crypto／store／admin suite 通過，既有三組 checks 通過。
- [x] 缺檔、格式、ACL、停用帳號、管理權限及寫入失敗皆有隔離測試證據，沒有建立真實 ProgramData 帳號。
- [x] commit／push 完成，worktree 乾淨，upstream 同步，建立並確認此分支的唯一 PR，記錄 URL，註明正式啟動入口待 Phase 3。
- [x] PR #7 已確認合併，兩端 Phase 2 分支已清理，具備 Phase 3 開工條件。

## Phase 3：桌面登入整合

**Branch:** `codex/login-phase-3-desktop-gate`。**開始條件:** Phase 2 PR 已合併，從更新後的遠端預設分支開分支。**交付界線:** 同一 PR 一併完成登入、設定模式入口、啟動與命令保護、登出及既有測試調整。

### Task P3-T1：登入 ViewModel、登入視窗及節流

**Files:** Create `LoginWindow.xaml/.cs`、`ViewModels/LoginViewModel.cs`、`Services/AuthenticationDiagnosticLog.cs`，Create `LoginChecks.cs`。

**Interfaces:**

- `LoginViewModel(IAuthenticationService authentication, TimeProvider timeProvider, AuthenticationDiagnosticLog diagnostics)`。
- `LoginCommand` 為 `IAsyncRelayCommand<SecureString>`，處理入口 `LoginAsync(SecureString password, CancellationToken) -> Task`，建立／釋放 LoginRequest，對外 `event Action<AuthenticatedUser>? LoginSucceeded` 只帶成功身分。
- `CancelLoginCommand` 呼叫 `Cancel()`，`event Action? Cancelled` 通知協調器退出，`Dispose()` 取消、解除倒數及清理。
- 狀態為 Username、IsBusy、StatusMessage、RemainingCooldownSeconds，`AuthenticationDiagnosticLog` 只保存安全事件分類及選擇性的 UserId，程序內使用，不持久化 credential。

- [x] 寫 `EmptyAndDuplicateSubmission`：空欄不呼叫 service，快速兩次提交只有一次驗證，IsBusy 期間欄位與登入按鈕停用，取消可用。
- [x] 寫 `ThrottleAndRecovery`：失敗 5 次後 30 秒不驗證，用 CheckSupport 中自製的 ManualTimeProvider 推進至期限後恢復，成功重設計數，不新增時間測試套件，也不依靠真實 sleep 30 秒。
- [x] 寫 `CancelledLateSuccessIgnored`：fake service 延後成功，取消後不觸發 LoginSucceeded，例外時不 Crash、IsBusy 恢復，失敗／取消／成功都要求清空 PasswordBox。
- [x] 執行 login suite，確認失敗，再實作狀態機與視窗。
- [x] 視窗約 440 × 360 DIP，使用既有 brushes／button styles，Tab、Enter、Esc、關閉鈕符合規格。code-behind 僅負責 SecurePassword 複本交付、密碼清空及視窗事件，不做帳密驗證。
- [x] 登入流程維護 request generation 與 CancellationTokenSource，await 完成後再檢查取消／generation，只有目前有效請求可發成功事件。密碼錯誤／未知／停用統一文案，設定錯誤使用規格指定提示。
- [x] 重跑 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite login`，檢查 diagnostics 的 ToString／訊息不含 credential、hash、salt。

### Task P3-T2：啟動協調器與視窗生命週期

**Files:** Create `Services/ApplicationSessionCoordinator.cs`、`Services/MainWindowFactory.cs`，Modify `App.xaml`、`App.xaml.cs`，Create `StartupChecks.cs`。

**Interfaces:**

- `StartupMode`：Normal／ConfigureLocalAccount，僅接受無參數或唯一的 `--configure-local-account`，其他參數顯示安全錯誤並退出，不以參數切換認證來源。
- `MainWindowFactory.Create(IUserSession session) -> MainWindow`：沿用現有 output 路徑解析及服務組裝，延後到登入成功才呼叫。
- `ApplicationSessionCoordinator(Application app, IAuthenticationService authentication, Func<IUserSession, MainWindow> mainWindowFactory, Func<LocalAccountConfigurationWindow> configurationWindowFactory, TimeProvider timeProvider, AuthenticationDiagnosticLog diagnostics)`，`Start(StartupMode)`、`RequestLogout()`、`Shutdown()`、`Dispose()`，對外只提供 IUserSession。
- `App` 增加 `protected virtual ApplicationSessionCoordinator CreateCoordinator()`，讓測試組裝的 TestApp 透過 override 注入 fake，正式 EXE 固定使用 Local adapter，不讀取測試旗標。

- [x] 寫 `StartupCreatesOnlyLogin`：經 App 正式 OnStartup 流程，main factory 呼叫次數 0、output 不存在，成功後 factory 恰好 1 次。
- [x] 寫 `SwitchAndExitLifetime`：成功關閉 LoginWindow 不退出，取消完全退出，主視窗初始化失敗清 session 並可重試，主視窗已存在不能自動放行。
- [x] 寫 `StartupModesAndLateResults`：管理模式未提升即拒絕、管理視窗關閉不開主視窗，取消與晚到成功交錯不留下主視窗。每案 STA 子程序，禁止測試以 app.Run(mainWindow) 繞過正常入口。
- [x] 執行 startup suite，確認行為缺失而失敗。
- [x] App.xaml 設定 OnExplicitShutdown，OnStartup 只建立協調器、解析模式及 Start。將原有 MainViewModel 組裝移至 MainWindowFactory，刪除原有 MainWindow 非 null 的直接返回邏輯。
- [x] 協調器採明確狀態 Starting／Login／OpeningMain／Main／ReturningToLogin／Closing／Admin，成功、取消、退出及登入／主視窗 Closed 事件都必須依目前狀態處理，初始化失敗 Dispose 已建立資源。
- [x] 重跑 `dotnet run --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite startup`。P3-T3 前 RequestLogout 先不公開給主畫面按鈕，維持可建置的階段成果，必須完成 P3-T3 才能送本 Phase PR。

### Task P3-T3：操作入口、目前使用者與登出清理

**Files:** Modify `MainViewModel.cs`、`MainWindow.xaml/.cs`、`ApplicationSessionCoordinator.cs`、`MainWindowFactory.cs`，Modify Phase2Checks／WpfChecks 的 `Program.cs`，Create `AccessChecks.cs`。

**Interfaces:**

- MainViewModel constructor 在既有依賴後增加 `IUserSession session`，新增唯讀 CurrentUserDisplayName、LogoutCommand、`event Action? LogoutRequested` 及 `Dispose()`。
- `LogoutCommand.CanExecute = session.IsAuthenticated && !IsBusy`，入口再檢查，觸發 LogoutRequested，協調器執行 RequestLogout。
- 六個受保護操作維持原命令名稱，CanExecute 增加登入條件，所有實際入口檢查登入與 session.Generation。
- MainWindowFactory 訂閱 LogoutRequested，所有訂閱以工作區 Dispose／Closed 解除，不能在登出後持有舊 ViewModel。

- [x] 寫 `SignedOutCommandsHaveNoEffects`：各命令 CanExecute=false，直接 Execute／ExecuteAsync 亦不開對話框、不呼叫 loader／parser／writer，OpenOutputFolder 不建立目錄或啟動 Explorer。
- [x] 寫 `SessionChangesDuringDialogAndAwait`：對話框返回或背景步驟完成前 generation 變更，不讀新檔、不寫後續結果、不更新舊 UI，用 fake services 及完成信號控制時序。
- [x] 寫 `LogoutClearsWorkspaceButKeepsFiles`：登出後 session 清空、Documents／Logs／SelectedDocument 清理，WebView2 Dispose、重新登入新工作區，事先建立的 output 檔仍存在。`BusyLogoutRejected`：忙碌時命令及 RequestLogout 都拒絕。
- [x] 執行 access suite，確認失敗，再實作所有入口檢查，不能只依賴 RelayCommand 的 CanExecute。
- [x] 在對話框返回後、await 後、寫入與 UI 更新前重新確認 session generation，工作區持有 lifetime CTS，正常關閉／Dispose 時取消並忽略晚到的 UI 更新，將 token 傳給既有支援取消的 service。
- [x] 登出順序為停用操作／Clear session → Dispose 工作區／關閉 MainWindow → 新建 LoginViewModel／LoginWindow，協調器區分退出和登出，不改寫 output ACL 或刪除檔案。
- [x] 調整 Phase2Checks 與 WpfChecks constructor，注入測試自有已登入 FakeUserSession，WpfChecks 改由 TestApp.CreateCoordinator＋fake 認證服務經登入流程進入主畫面，避免移除原 guard 後重複開窗。更新結束邏輯以配合 OnExplicitShutdown。
- [x] 重跑 access、Phase2Checks 與 WpfChecks，確認原操作行為維持，新增使用者名稱與登出 UI 的 binding 檢查。

### Phase 3 驗證與 PR

- [ ] P3-T1、P3-T2、P3-T3 全部步驟完成，Solution build、AuthenticationChecks all suite 及既有三組 checks 通過。
- [ ] L1～L13 的自動化情境通過，正式入口登入前不建立主畫面，直接命令不可繞過，已驗證設定模式、登出與視窗生命週期。
- [ ] commit／push 完成，worktree 乾淨，upstream 同步，建立並確認此分支的唯一 PR，記錄 URL，列出發佈 EXE／實際部署驗證將於 Phase 4 完成。
- [ ] 前述 PR 確認合併後，才允許開始 Phase 4。

## Phase 4：驗證與交付

**Branch:** `codex/login-phase-4-verification`。**開始條件:** Phase 3 PR 已合併，從更新後的遠端預設分支開分支。**交付界線:** 完成累積回歸、發佈驗證及可追溯交付文件，本 Phase 發現的驗證缺漏或缺陷在同一分支修正，不另開 Task 分支／PR。

### Task P4-T1：完整回歸與驗收補強

**Files:** Complete AuthenticationChecks 的 `Program.cs` 與各 suites，必要修正限於驗證揭露的登入相關產品／測試檔案。

**Interfaces:** 不新增產品介面，all suite 順序執行各 suite，任何失敗令 exit code 非零。保留每項實際結果供 P4-T3 記錄。

- [ ] 先執行 all suite，確認沒有 skipped case 或以 fake 成功掩蓋 LocalAuthenticationService／正式啟動入口，補齊下方 L1～L14 對應的缺漏檢查，再實作必要修正。
- [ ] 在根目錄依序執行以下完整驗證，保存每步 exit code 與摘要，失敗先修正，不繼續宣稱完成：

```powershell
dotnet restore ProgramMigrationAnalyzer.sln
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet build ProgramMigrationAnalyzer.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite all
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.Phase2Checks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.RegressionChecks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.WpfChecks
```

預期：Build 0 error，沒有新增的未處理平台警告，四組 checks 全部 exit code 0，報告顯示實際通過案例，不僅顯示程序有啟動。

### Task P4-T2：發佈 EXE 與桌面部署驗證

**Files:** 使用現有 `publish.bat`，只有發佈／桌面驗證揭露問題時才修改登入相關程式或發佈腳本。可控的驗證紀錄先保留在本 Phase 的 `.codex-tmp/`。

**Interfaces:** 沿用同一 Local provider、正常啟動及管理入口，不新增部署用免登入開關。

- [ ] 以既有 `publish.bat` 發佈 Windows x64 自包含 EXE，確認仍可免安裝 .NET 使用，不要改為全程要求管理者的 application manifest，只有帳號設定需要提升權限。
- [ ] 在已授權的測試電腦／VM 驗證部署：管理者互動建帳 → 一般權限 EXE 登入 → Scenario A／B／C → 登出 → 重新登入 → 退出，另從不同工作目錄啟動 EXE，確認認證路徑仍為同一 ProgramData。未獲部署授權時，記錄此驗證未執行，不碰真實帳號檔。
- [ ] 桌面檢查 Enter／Tab／Esc、取消及視窗切換，確認視窗沒有短暫洩露主畫面、無殘留程序，既有 Markdown HTML／fallback 行為正常。

- [ ] 若未取得部署授權，P4-T2 真實部署驗證維持未完成，回報具體阻礙，Phase 4 不能標記已驗收或送 ready PR，待必要驗證完成後再送審。

### Task P4-T3：交付文件、差異檢查與 PR 準備

**Files:** Modify `README.md`，Create `docs/Login_Verification_and_Delivery.md`，更新本計畫的 Task／Phase 狀態及 PR 紀錄。

**Interfaces:** 不新增產品介面，文件使用實際驗證結果，不把尚未執行或未通過的情境寫成已通過。

- [ ] README 加入首次建帳、`--configure-local-account`、一般登入、重設／停用生效時點、登出及本機限制，不寫入實際密碼或帳號檔。交付文件保存 L1～L14 結果與已知限制。
- [ ] 執行 `git diff --check`、檢查預期檔案清單及敏感資料，不 stage temp、既有截圖／簡報或任何 ProgramData 資料，記錄目前分支及未提交狀態。

### Phase 4 驗證與 PR

- [ ] P4-T1、P4-T2、P4-T3 全部步驟完成，L1～L14 與 Scenario A／B／C 均有實際驗證證據，文件與發佈方式一致。
- [ ] 若本 Phase 修改產品程式，重新跑受影響測試及完整必需回歸，結果通過後才送 ready PR。
- [ ] commit／push 完成，worktree 乾淨，upstream 同步，建立並確認此分支的唯一 ready PR，記錄 URL，已有 open PR 則沿用，不能新增第二個。
- [ ] 回報四個 Phase 的 Task、分支、PR 與驗證狀態，最終 PR 尚未合併時狀態為「已送審」，不宣稱已合併交付。

## 驗收對照

| 規格情境 | 責任任務 | 證據 |
| --- | --- | --- |
| L1 未登入啟動 | P3-T2、P4-T2 | StartupCreatesOnlyLogin、EXE 桌面流程 |
| L2 有效帳號登入 | P2-T1、P3-T2、P4-T2 | ValidEnabledAccountOnly、主視窗恰好一次、Scenario A／B／C |
| L3 錯誤／未知／停用 | P2-T1、P3-T1 | InvalidCredentials 一致、PasswordBox 清空 |
| L4 空白／重複提交 | P3-T1 | EmptyAndDuplicateSubmission |
| L5 取消／晚到成功 | P3-T1、P3-T2 | CancelledLateSuccessIgnored、StartupModesAndLateResults |
| L6 帳號檔／ACL 不合法 | P2-T1 | InvalidAccountFileFailsClosed、AclAndInterruptedWrite |
| L7 登出／新工作區 | P3-T3 | LogoutClearsWorkspaceButKeepsFiles |
| L8 忙碌時登出 | P3-T3 | BusyLogoutRejected |
| L9 直接呼叫受保護命令 | P3-T3 | SignedOutCommandsHaveNoEffects、SessionChangesDuringDialogAndAwait |
| L10 切換／退出 | P3-T2、P3-T3、P4-T2 | SwitchAndExitLifetime、桌面退出與程序檢查 |
| L11 管理模式 | P2-T1、P2-T2、P3-T2、P4-T2 | NonElevatedCannotManage、ManageAccountLifecycle、部署 ACL 驗證 |
| L12 salt／重設／密碼原樣 | P1-T2、P2-T2 | SaltAndResetSemantics、PasswordUnicodeAndWhitespace |
| L13 5 次失敗／30 秒節流 | P3-T1 | ThrottleAndRecovery |
| L14 重啟／發佈 EXE | P3-T2、P4-T2 | 重新啟動必須登入、不同工作目錄、無測試旗標 |

## Phase／PR 追蹤紀錄

以下為計畫狀態，實作時填入實際 commit、檢查結果與 PR URL，不能用預定名稱當作已建立的證據。

| Phase | Task 狀態 | 分支狀態 | 驗證 | PR URL／狀態 | 合併狀態 |
| --- | --- | --- | --- | --- | --- |
| 1 | 2／2 完成並逐一推送 | 已清理，原 `codex/login-phase-1-foundation` | contracts 5 組、crypto 7 組及既有回歸通過 | [PR #6](https://github.com/AxeenWang/ProgramMigrationAnalyzer/pull/6)，MERGED | `e33e33f` |
| 2 | 2／2 完成並逐一推送 | 已清理，原 `codex/login-phase-2-local-accounts` | contracts 5、crypto 7、store 11、admin 6 及既有回歸通過 | [PR #7](https://github.com/AxeenWang/ProgramMigrationAnalyzer/pull/7)，MERGED | `d890707` |
| 3 | P3-T1～P3-T3 Task 驗證完成，3／3 | codex/login-phase-3-desktop-gate | login 5／5、startup 8／8、access 6／6，累積 48 組及既有回歸通過 | 未建立 | 未合併 |
| 4 | 0／3 完成 | 未建立 | 未執行 | 未建立 | 未合併 |

Phase 的正常狀態依序為「未開始 → 實作中 → 驗證通過 → 已送審 → 已合併」。Task 全部完成且必要檢查通過，才可標記「驗證通過」，PR URL 與遠端狀態查證成功，才可標記「已送審」。未能送 PR 時仍是未交付，不能略過此狀態。下一 Phase 的開工條件為前一 Phase「已合併」。

2026-10-01 清理紀錄：PR #6 於 `2026-10-01T01:35:27Z` 合併，merge commit 為 `e33e33f7e07dc37aef8c162d368b119da0d8a38e`。依使用者指示，先清理全部主題分支，本機與遠端只保留 `main`。Phase 2～4 分支維持未建立。原工作區的未提交內容保留在相同 commit 的 detached HEAD，尚未合併的 `codex/ignore-references` 提交已保存並驗證專案暫存區的 Git bundle。其後才修正 `.references/` 忽略與分支建立時機，使用者明確授權此維護變更直接提交並推送到 `main`，不開始新的 Phase。

### Phase 1 執行紀錄

| Task | commit／push | 驗證 |
| --- | --- | --- |
| P1-T1 | `55c5b17f17839b9cf908299498aa42b69b3050b2` 已推送 | 編譯成功的初始實作有 4／5 行為檢查失敗，完成後 contracts 5／5 及既有回歸通過 |
| P1-T2 | `e008a54c4964d82f9caf446522775220640c6a29` 已推送 | 初始實作 crypto 7／7 失敗，完成後 7／7 通過，另以失敗測試修正 Unicode 帳號轉小寫邊界 |

最終 Solution build 為 0 warnings／0 errors，contracts／crypto 合計 12 項通過，既有 Phase2Checks／RegressionChecks／WpfChecks 均通過。PBKDF2 另外對照獨立產生的測試向量，store 尚未實作而以 exit code 2 拒絕執行，沒有假通過。WpfChecks 觀察到既有 WebView2 E_UNEXPECTED，純文字備援路徑通過，本輪不作完整 WebView2 呈現驗證。

整個分支由實作者自審，未發現需修正的問題。依本計畫的代理分工限制，本次未使用獨立代理審查，獨立審查留待 PR。原工作區的既有截圖及簡報均保留，沒有提交 agent temp、真實帳號檔或建置產物。

執行調整與成本：

- 手動建立已授權位置的 worktree，因 App API 無法從此 chat 指定該 repo 與路徑，成本為不會自動登錄至 App 的 managed worktree 清單。
- 暫存進度使用 `.codex-tmp` 人工紀錄，遵守專案暫存規則及 Phase／Task 標題，成本為需人工維護，未使用 skill 的自動 ledger helpers。
- 隔離 checkout 採 standalone bootstrap，原因見開工決議，成本為不自動匯入外部治理 anchor，仍遵守使用者已指定的隔離規則。
- 使用作者自審，遵守本計畫對代理分工的授權限制，成本為沒有獨立代理的第二次檢查，仍須由 PR 審查確認是否可合併。

### Phase 2 執行紀錄

| Task | commit／push | 驗證 |
| --- | --- | --- |
| P2-T1 | d368e5b9a53bdb74102c011805542e5d6aa3e03c 已推送，0／0 後才開始 P2-T2 | store 初始 8／8 失敗，完成後 8／8 通過，累積回歸通過 |
| P2-T2 | 8a3a7430833dd0d6604af0af78d5685f3c283902 已推送，0／0 後才開始 Phase 審查 | admin 初始 6／6 失敗，完成後 6／6 通過，包含 STA 設定視窗及取消檢查 |

Phase 自審另以三個失敗檢查修正鎖檔開啟前驗證、4 MiB 帳號檔大小門檻及替換後驗證失敗的舊檔還原。更新用同目錄受保護備份保留舊資料，排他鎖涵蓋替換、還原及清理，成功後清理備份。還原期間的鎖保留另以實際隔離檔案的競爭開啟斷言確認，先失敗再修正。若作業系統連還原也拒絕，保留受保護備份供管理者復原，不宣稱能克服磁碟或作業系統故障。

Phase 審查修正已提交並推送 40ba222348b7133c6ebe3aff227be88f301cae6b。PR #7 建立時為 OPEN／ready 並已附加 chat。Phase 3 開工已重新查證為 MERGED，mergedAt `2026-10-01T03:32:13Z`，merge commit `d890707a55ec92096b5015162f667955164251da`。指定續作 worktree 曾切回 main 並 fast-forward 至此 commit，兩端 Phase 2 分支已刪除，2026-10-01 Phase 3 fetch 後 main 與 origin/main 仍為 0／0 且乾淨，從此基底建立 Phase 3 分支。原工作區未操作。

最終 Solution build 為 0 warnings／0 errors，AuthenticationChecks 為 contracts 5／5、crypto 7／7、store 11／11、admin 6／6，共 29 組，Phase2Checks、RegressionChecks、WpfChecks 通過。WpfChecks 仍觀察到既有 WebView2 E_UNEXPECTED，純文字備援路徑通過，完整 WebView2 呈現未驗證。

帳號、鎖檔、hash、視窗影像及驗證產物均僅存在隔離暫存區，未提交。測試使用 fake policy，沒有存取真實 ProgramData 帳號或修改部署 ACL。Windows ACL adapter 的真實部署整合仍待另獲授權驗證。沙箱內原子替換遇到 0x80070005，相同隔離檢查經核准執行通過。

設定視窗已以實際 STA 子程序驗證並檢視影像，帳密確認、背景計算、重複送出、模式切換及關閉清理通過。服務保留計畫的 CreateOrResetAsync，另提供 CreateAsync／ResetAsync，讓視窗四種模式拒絕重複建帳及未知帳號重設。一般權限拒絕管理，管理元件不建立工具 session 或分析主視窗。

依使用者限制，由同一實作者順序執行及自審，未使用代理分工，獨立審查留待 PR。暫存進度沿用專案 .codex-tmp，未使用 skill 的其他 scratch 位置。正式 --configure-local-account 入口、登入畫面、啟動門檻與登出均待 Phase 3，此 Phase 不代表產品登入已完成。Phase 3／4 分支未建立。

## 完成與交接條件

- [ ] 四個 Phase 共十個 Task 完成，規格 L1～L14 有可追溯證據，既有分析／轉譯回歸通過。
- [ ] 四個 Phase 各有一個專屬分支與一個經查證的 PR，Phase 追蹤紀錄完整，最終合併狀態據實回報。
- [ ] README 與交付紀錄可讓另一位工程師在新電腦完成建帳及登入，未驗證的真實部署明確標示。
- [ ] 正式 EXE 只組裝 Local provider，日後正式認證工作另行提供契約並實作 adapter，不做自動 fallback。
- [ ] 不宣稱本機登入能抵抗 EXE 修改、加密 output 或提供集中停權。

下一步由使用者開始指定 Phase 後，依本計畫執行該 Phase 的所有 Task、驗證及 commit／push／PR。建議在同一個 Active Target 由同一實作者依序使用 `superpowers:executing-plans`，因各 Task 共用認證、session 與視窗生命週期介面，每完成一個 Phase 並建立 PR 即回報，不跨越前一 PR 的合併條件。Phase 1 已依開工決議開始，帳號與正式登入入口仍依後續 Phase 交付。

## 技術依據

密碼與視窗生命週期依據沿用規格第 37.11 節。Windows ACL 操作使用 [Microsoft FileSystemAclExtensions](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemaclextensions?view=net-10.0)，提升權限檢查須依目前 Windows token，而非只判斷帳號是否屬於管理者群組，參考 [Microsoft 的 UAC／split token 說明](https://devblogs.microsoft.com/oldnewthing/20241003-00/?p=110336)。

### Phase 3 執行紀錄

P3-T1：先加入登入檢查，建置因缺少 LoginViewModel／LoginWindow／diagnostics 而失敗，完成後 login 5／5 通過。涵蓋空值、直接重複提交、假時間冷卻與成功重設、取消後晚到成功、安全例外分類及 STA PasswordBox 清理。Solution build 0 warnings／0 errors，contracts 5、crypto 7、store 11、admin 6、login 5，共 34 組，既有三組回歸通過。sandbox 原子替換限制重現，以相同隔離檔案及 fake policy 在允許的提權環境重跑通過，未操作真實 ProgramData。依使用者指示由同一實作者執行，未使用代理。

P3-T1 已提交並推送 afc89d6ed0b51d35af84389a3732fc7b4ec55c46，確認 upstream 0／0、工作區乾淨後才開始 P3-T2。

P3-T2：startup 先因缺少 coordinator／App hook 失敗，完成後 startup 8／8 通過，涵蓋只建立登入、主畫面初始化失敗重試、取消後晚到成功、預先存在的 MainWindow 不放行、管理權限及隔離、唯一參數與非法參數安全退出。Solution build 0 warnings／0 errors，累積認證 42 組及 Phase2Checks／RegressionChecks 通過。既有 WpfChecks 的登入改造依計畫留至 P3-T3，本 Task 不將舊直接 app.Run(mainWindow) 當作門檻證據。

P3-T2 調整：WPF 產生的 App.InitializeComponent 無法載入至衍生 TestApp，共用樣式移至編譯的 Resources/ApplicationResources.xaml，正式 App 與 TestApp 載入相同資源。測試宿主用自有程序路由選擇 STA 案例，正式 App 僅解析無參數或唯一管理參數，不讀測試環境變數。coordinator 型別為 public 以支援 protected CreateCoordinator，constructor 保持 internal。

P3-T2 已提交並推送 06d01470a9c8555acbab7768e7486c47211a1eb8，確認 upstream 0／0、工作區乾淨後才開始 P3-T3。

P3-T3：access 先因缺少 session constructor、LogoutCommand／Dispose 而失敗，完成後 access 6／6 通過，包含六個入口的 CanExecute 與直接呼叫、三種對話框返回、八個背景步驟的晚到結果、token 取消、訂閱釋放、忙碌登出拒絕及完整 STA 登出／再次登入生命週期。MainViewModel 綁定建立時的 session Generation，session 改變或關閉即取消 lifetime token 並清理記憶體文件。MainWindow Dispose 釋放 WebView2 及事件，output 檔保留。

MainWindowFactory 訂閱 LogoutRequested，正常 composition root 使用回呼連至 coordinator。共用樣式及唯一 provider 維持一致。測試的 factory 接受隔離 output 路徑，不加入正式命令列旗標。WpfChecks 使用測試組裝的 TestApp override 經正式 OnStartup、fake authentication 及 coordinator 進入主畫面，保留所有原功能檢查，另驗證使用者名稱、登出、新工作區及輸出保留。App 對 WpfChecks 加入 InternalsVisibleTo，僅限測試組裝，沒有公開 session setter。

P3-T3 Solution build 0 warnings／0 errors，認證累積 48 組及 Phase2Checks／RegressionChecks／WpfChecks 通過。受限環境觀察到既有 WebView2 E_UNEXPECTED 且純文字備援通過。允許的提權環境則完成 Markdown DOM 檢查及渲染截圖，這是本機該環境的實際渲染證據，不擴張為發佈環境驗收。Windows ACL 真實部署及發佈 EXE 驗證仍待 Phase 4。

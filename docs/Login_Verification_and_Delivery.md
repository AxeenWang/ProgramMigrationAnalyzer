# 本機登入驗證與交付紀錄

日期：2026-10-01。範圍為登入 Phase 4，分支 `codex/login-phase-4-verification`，基底 `82474c8`。P4-T1 的正式入口補強已推送 `9d0e2d2`，P4-T2 的部署修正已推送 `a12b7a4`，完成紀錄已推送 `6d80a48`。目前文件屬 P4-T3，最新 Task、PR 與合併狀態見[實作計畫](superpowers/plans/2026-10-01-local-login-implementation-plan.md)。

規格依據為 [ProgramMigrationAnalyzer_Codex_Spec.md](ProgramMigrationAnalyzer_Codex_Spec.md) 第 37 節。同一實作者依 Task 順序執行、自審、提交與推送，未使用代理，獨立審查由 PR 進行。原 checkout 的既有資料保留。

## 操作與交付方式

首次建帳、提升權限的 `--configure-local-account` 入口、正常登入、重設／停用、登出與帳密規則見 [README](../README.md#本機帳號設定與登入)。

以既有 `publish.bat` 發佈 Windows x64 自包含單檔 EXE，收件端逐機設定本機帳號。管理入口需已提升權限的 Windows 管理者，一般使用保持 `asInvoker`。帳號檔固定為 `%ProgramData%/ProgramMigrationAnalyzer/auth/users.json`，不隨目前工作目錄改變。

EXE 不包含預設帳號或密碼，也沒有免登入參數。帳號檔、salt、hash、密碼、桌面擷取、暫存、output 及 publish 產物均不提交。實際密碼與 UAC 由使用者手動處理。

## 自動化驗證

2026-10-01 `06:00:14Z` 起，在部署修正後執行以下六步，exit code 全部為 0。其後只有文件及證據紀錄變更。

```powershell
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet restore ProgramMigrationAnalyzer.sln -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build ProgramMigrationAnalyzer.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v quiet
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.AuthenticationChecks -- --suite all
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.Phase2Checks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.RegressionChecks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.WpfChecks
```

| 檢查 | 實際結果 |
| --- | --- |
| Solution build | 0 warnings、0 errors |
| contracts | 5／5 |
| crypto | 8／8 |
| store | 13／13 |
| admin | 6／6 |
| login | 5／5 |
| startup | 13／13 |
| access | 6／6 |
| AuthenticationChecks 合計 | 56／56，沒有 skipped case |
| Phase2Checks | 資料夾、輸出命名、背景分析、切換、重載、失敗與預覽提示通過 |
| RegressionChecks | 十個樣本、Scenario A／B／C、.NET 8／10、4GL 草稿 C# 編譯及邊界輸入通過 |
| WpfChecks | 登入門檻、使用者／登出、新工作區、輸出保留、原分析／轉譯／Diff／儲存流程通過 |

[LocalStartupChecks](../tests/ProgramMigrationAnalyzer.AuthenticationChecks/LocalStartupChecks.cs) 透過正式 `App.OnStartup`、coordinator、真實 LocalAuthenticationService、LocalAccountStore 及 PBKDF2，使用隔離帳號檔與測試專用密碼。只替換 ACL／提升權限 policy，不以 fake authentication 代替成功登入。其他可控非同步測試仍使用明確的測試替身，沒有加入正式 EXE 開關。

新增的兩個 Windows ACL 回歸使用記憶體 security descriptor，並不寫入真實部署 ACL。根目錄 Modify 誤拒絕先得到 RED，再修正至 store 13／13。密碼規則先得到 crypto RED，再確認 8／128 可、7／129 不可，8 字元符號密碼可經 PasswordBox 儲存及驗證。

## 發佈 EXE 與 SWANG-PC 部署

使用者指定 SWANG-PC 並授權測試帳號、工具目錄、auth 目錄及 users.json 的 ACL 操作。主機為 Windows build 26200、x64。部署前該工具帳號檔不存在，管理者在修正版視窗成功建立一個啟用帳號，顯示「帳號設定已儲存，下次登入生效」。

| 修正版產物 | 實際值 |
| --- | --- |
| 路徑 | `publish/win-x64-single-file/ProgramMigrationAnalyzer.App.exe` |
| 發佈 | 原 publish.bat，exit code 0，Windows x64、自包含、單檔 |
| 大小 | 192,741,661 bytes |
| SHA-256 | `D8A9173D415D2527AA94717D741D06B4AEC5245B8E03FAD4E31B46CD46636611` |
| PE／manifest | AMD64，requestedExecutionLevel 為 asInvoker |
| 內含 frameworks | Microsoft.NETCore.App 10.0.12、Microsoft.WindowsDesktop.App 10.0.12 |

此 EXE 在提交部署修正前由相同工作區程式碼發佈，修正後來源隨 `a12b7a4` 提交。未宣稱是在該 commit 之後重新建置的產物。

實際程序的 DOTNET_ROOT／DOTNET_ROOT_X64 指向不存在的任務路徑，PATH 僅含 SystemDirectory。host trace 顯示 internal fxr、internal hostpolicy、self-contained app 及內含 10.0.12 runtime。主機仍安裝 SDK，這證明使用內含 runtime，未完成沒有任何 .NET 安裝的乾淨 VM 實測。

帳號檔 schema 為 1，只有一個啟用的測試帳號。僅核對欄位、演算法及長度，PBKDF2-HMAC-SHA256、600,000 iterations、16 bytes salt、32 bytes hash，未輸出實際 salt／hash 或密碼。工具目錄、auth 目錄及帳號檔的 owner 為 Administrators，ACL 停止繼承，SYSTEM／Administrators FullControl，Users 的目錄 ReadAndExecute、檔案 Read，沒有其他 grants。

一般 token 請求 write handle 被拒絕，沒有寫入或截斷檔案。正常登入、分析、登出及重登前後帳號檔指紋相同。ProgramData 與磁碟根的 ACL 二進位指紋前後相同，沒有更改作業系統祖先 ACL 或 Windows 帳號。測試帳號保留於這台已授權電腦。

部署揭露 C:/ 根目錄上的直接 Modify ACE 被原 validator 的 DELETE 判斷誤拒絕。修正只排除本機磁碟根本身不能被刪除的 DELETE，根目錄 DELETE_CHILD、ACL 控制、可信 owner、DACL，以及一般祖先、UNC 根、工具／auth／檔案與 reparse point 防護仍受檢查。

## L1～L14 結果與證據界線

下表各項有實際執行的自動化證據，桌面欄只列出 SWANG-PC 觀察。隔離測試的 ACL 替身不代表真實 Windows 部署，部署證據由前述真實 token／ACL 與正式 EXE 取得。

| 編號 | 自動化證據 | 發佈 EXE 桌面觀察與限制 |
| --- | --- | --- |
| L1 未登入啟動 | StartupCreatesOnlyLogin、LocalProviderStartupAndReauthentication | 正常入口只有登入視窗，未開主畫面或自動建帳 |
| L2 有效帳號登入 | ValidEnabledAccountOnly、LocalProviderStartupAndReauthentication | 使用者手動登入後恰好一個主畫面，顯示名稱正確，Scenario A／B／C 可用 |
| L3 錯誤／未知／停用 | ValidEnabledAccountOnly、LoginWindowClearsPasswords、LocalProviderStartupAndReauthentication | 三種失敗一致、密碼清空由自動化核對，未逐一操作真實部署帳號 |
| L4 空白／重複提交 | EmptyAndDuplicateSubmission | 空白 Enter 留在登入，快速重複請求由自動化核對 |
| L5 取消／晚到成功 | CancelledLateSuccessIgnored、CancelAndLateSuccess、OpeningMainCancellationDisposesCandidate | Esc、取消及標題列關閉皆退出，晚到成功以可控自動化核對 |
| L6 缺失／損壞／ACL | InvalidAccountFileFailsClosed、LocalProviderConfigurationFailsClosed、Windows ACL 回歸 | 缺失設定提示管理者且不建帳，實際部署 ACL 及普通 token 拒寫另驗證，未破壞真實帳號檔 |
| L7 登出／重新登入 | LogoutClearsWorkspaceButKeepsFiles、LocalProviderStartupAndReauthentication | 同程序同帳號重登只有新主畫面，來源、Tags、統計及 log 清空，六個 output 項目保留。不同帳號及所有報告／預覽屬性由自動化核對 |
| L8 忙碌登出 | BusyLogoutRejected | 捕捉分析忙碌時登出停用及等待提示，完成後恢復，轉譯／其他忙碌分支由自動化核對 |
| L9 直接命令 | SignedOutCommandsHaveNoEffects、SessionChangesDuringDialog、SessionChangesDuringAwait | 六個入口及過期非同步結果由自動化核對，未以正式 EXE 注入呼叫 |
| L10 切換／退出 | SwitchAndExitLifetime、LocalProviderStartupAndReauthentication | 登出只剩登入，重登不誤退出。最後關閉主畫面 exit code 0，App 程序與視窗零殘留 |
| L11 管理與 ACL | NonElevatedCannotManage、ManageAccountLifecycle、ConfigurationWindowIsolation | 一般權限管理入口停用，提升權限成功建帳，真實 ACL 通過。重設／啟用／停用未另在桌面操作 |
| L12 salt／重設／密碼原樣 | SaltAndResetSemantics、PasswordUnicodeAndWhitespace、EightCharacterPasswordWithSymbols、ManageAccountLifecycle、LocalProviderStartupAndReauthentication | 兩帳號 salt／hash 差異、舊密碼失效、新密碼有效及原樣比對由真實 provider 的隔離自動化核對，未建立第二個部署帳號 |
| L13 五次失敗／30 秒 | ThrottleAndRecovery、CountdownUsesUiDispatcher | 程序內節流及時間邊界由 TimeProvider 自動化核對，未另手動等待桌面倒數 |
| L14 重啟／發佈入口 | StrictStartupModes、InvalidModeExits、正式 OnStartup 檢查 | 重啟仍須登入，兩種工作目錄使用同一 ProgramData，非法 --skip-login 被拒絕，沒有自動登入 |

Enter／Tab／Esc、取消、缺失設定及非法參數的早期桌面觀察對應舊 EXE，SHA-256 為 `9352D2ED480D83762BF23C56B29C503F941B9C1B1699D791C12C546A4CB33C9B`。Tab 依畫面可見焦點核對順序為帳號、密碼、登入、取消。部署修正未改啟動或鍵盤程式，修正版完整自動化另重跑，並重新實測有效登入、建帳與 ACL、樣本、預覽、忙碌登出、重登、不同工作目錄及退出。未把早期桌面觀察寫成修正版逐項重播。

## 標準樣本、預覽與輸出保留

以指紋相同的工作區樣本副本，透過修正版 EXE 的檔案選擇、分析及轉譯操作取得下列結果。

| Scenario | 桌面結果 | output 產物 |
| --- | --- | --- |
| A：CustomerQuery.4gl 分析 | 4GL、3 個函式、3 段 SQL、3 個相依項目、Customer 標籤，score 86 | `CustomerQuery.4gl.2681f27cd9ee977d.analysis.md` |
| B：LegacyApiClient.cs 分析 | C#、5 個函式、1 段 SQL、5 個相依項目，UI／報告有 XML 與 WebRequest 問題，score 80 | `LegacyApiClient.cs.486542f39b21d41e.analysis.md` |
| C：CustomerQuery.4gl 轉譯 | .NET 10，C# 預覽含 CustomerQueryService | `CustomerQuery.4gl.2681f27cd9ee977d.net10.cs` |

正常環境的 Markdown tab 實際顯示 WebView2 HTML，核對標題、段落及表格。另只對一個驗證程序將官方 WebView2 runtime path override 指向不存在的任務目錄，未更改系統 Runtime。登入並分析後出現無相容 Runtime、已改用純文字的提示，關閉提示仍可閱讀 Markdown。HTML 與純文字結果分別記錄。

最後一次同程序登出與重登共保留六個 output 項目，包含五個已產生檔案及既有 `.gitkeep`，檔名、長度及 SHA-256 前後一致，也保留使用者先前的兩個輸出。程序於 `2026-10-01T06:28:47Z` 正常退出，exit code 0，`06:28:56Z` 核對 App 程序為零。

## 限制與後續

- 乾淨 VM 未執行，SWANG-PC 上已驗證自包含產物、內含 runtime 與兩種工作目錄。
- 真實部署只有一個帳號，重設／停用、不同使用者、錯誤／未知／停用及 30 秒節流的完整邊界由自動化覆蓋，沒有全部在桌面重做。
- 目前只有 Local provider，沒有 SSO、帳號同步、角色矩陣、自動登入、磁碟 session 或閒置逾時。正式認證需另提供契約與 adapter，不自動 fallback。
- 重設或停用於下次登入生效，現有 session 不立即撤銷。output 未加密，登入不更改其 Windows 存取權限，也不宣稱可抵抗 EXE 修改。
- 診斷 Log 只存在程序內，不是持久稽核資料庫。密碼使用短生命週期載體及可清除 buffer，不宣稱所有 managed string 副本都能抹除。
- 分析、轉譯及 parser 原有限制與視覺設計待確認事項仍見 README 與既有 Phase 3 紀錄。Scenario 通過不代表轉譯完成語意等價移植。
- 最終 Phase 4 PR 需由使用者審查與合併，建立 ready PR 不代表已合併。

完整原始 log、UI 觀察、host trace、ACL 安全 metadata、輸出指紋與退出紀錄保留在當次 Git root 的 `.codex-tmp/2026-10-01_login-phase-4/`，未追蹤。可追溯的測試原始碼、產物指紋、結果摘要與[計畫紀錄](superpowers/plans/2026-10-01-local-login-implementation-plan.md)隨 PR 保存，重現時以自備測試帳密執行，不複製真實帳號資料。

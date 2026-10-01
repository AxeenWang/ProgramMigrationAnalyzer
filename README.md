# Program Migration Analyzer

程式移植分析儀是一套 Windows WPF 工具，目標是分析舊版 .NET Framework C# 與 Informix 4GL 原始碼，產生結構化分析、Markdown 報告與現代化 C# 程式碼草稿。

> 目前狀態：第一階段功能與第二階段可靠性修正已合併。第三階段的樣本回歸、WPF 畫面流程及實際桌面操作已通過，WebView2 HTML 預覽與失敗備援均已驗證。按鈕與頁籤的視覺設計仍待使用者確認。

公司簽章離線授權已接入正常啟動流程，Phase 5 的 T1～T6 已完成並推送，[PR #11](https://github.com/AxeenWang/ProgramMigrationAnalyzer/pull/11) 為 ready、等待審查。正式公司金鑰與 SWANG-PC 帳號部署、使用者完整桌面驗收、舊入口及退出檢查均已完成。客戶端須先匯入公司 Key，再以帳密登入，密碼最低 8 個字元，允許特殊符號。目前證據與未執行項目見[離線授權交付紀錄](docs/Offline_Authorization_Verification_and_Delivery.md)。Phase 4 的舊版本結果另保留在[登入驗證與交付紀錄](docs/Login_Verification_and_Delivery.md)。

## Features

- 開啟單一原始碼檔案或包含多個檔案的資料夾
- 自動辨識 C#、`.4gl` 與 `.per` 檔案
- 分析程式結構、函式呼叫、SQL、資料表與外部相依性
- 偵測常見的舊版 .NET API 與移植風險
- 產生規則式 Migration Assessment
- 產生並預覽 Markdown 分析報告
- 選擇 `.NET 8` 或 `.NET 10` 作為轉譯目標
- 產生現代化 C# 程式碼草稿與簡易 Diff
- 顯示分析、轉譯進度及執行 Log
- 公司簽章離線 Key、帳號登入、使用者顯示與登出
- 獨立公司發證工具，可建立帳號、重設密碼、啟用／停用並重新簽發 Key

## Architecture

核心流程如下：

```text
Source
  -> Parser
  -> Intermediate Analysis Model
  -> Analyzer
     -> Markdown Report Generator
     -> Migration Assessment
     -> Source Translator
```

C# 與 4GL parser 會先轉換為共用的 Intermediate Analysis Model，再由分析、報告及轉譯元件使用，避免將語言解析邏輯綁定在 UI。

## Solution Structure

```text
ProgramMigrationAnalyzer/
├─ ProgramMigrationAnalyzer.sln
├─ src/
│  ├─ ProgramMigrationAnalyzer.App/
│  ├─ ProgramMigrationAnalyzer.Core/
│  ├─ ProgramMigrationAnalyzer.Analysis/
│  ├─ ProgramMigrationAnalyzer.Parsers/
│  ├─ ProgramMigrationAnalyzer.Translation/
│  ├─ ProgramMigrationAnalyzer.Infrastructure/
│  └─ ProgramMigrationAnalyzer.LicenseIssuer/
├─ build/
│  └─ ProgramMigrationAnalyzer.PublishPreparation/
├─ samples/
│  ├─ 4gl/
│  └─ csharp-framework/
├─ tests/
│  ├─ ProgramMigrationAnalyzer.AuthenticationChecks/
│  ├─ ProgramMigrationAnalyzer.Phase2Checks/
│  ├─ ProgramMigrationAnalyzer.RegressionChecks/
│  └─ ProgramMigrationAnalyzer.WpfChecks/
├─ output/
└─ docs/
```

## Technology

- Windows Desktop WPF
- C# and `.NET 10`
- MVVM with `CommunityToolkit.Mvvm`
- Roslyn with `Microsoft.CodeAnalysis.CSharp`
- AvalonEdit
- Markdig
- Microsoft WebView2

## Requirements

以下為建置環境需求。發佈 EXE 的收件端需 Windows x64、公司簽發的授權 Key 與公司提供的帳密，匯入 Key 需 Windows 管理員權限，HTML 預覽另需 WebView2 Runtime。

- Windows operating system supported by the installed .NET 10 SDK and runtime
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime
- Visual Studio or another editor capable of building `.NET 10` WPF applications

## Build

使用下列命令還原與建置：

```powershell
dotnet restore ProgramMigrationAnalyzer.sln
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet build ProgramMigrationAnalyzer.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
```

本機 Phase 3 驗證使用單一 MSBuild 節點且停用節點重用，完成後沒有留下 `dotnet` 或 `MSBuild` 背景程序。

### 發佈單一 EXE

在正式專案根目錄發布。客戶端必須指定有效的公司 P-256 SPKI 公鑰，也可預先將公開公鑰放在本機 `config/authorization-public-key.pem`，再不帶參數執行 `publish.bat`。公鑰檔只供建置使用，執行時不能換外部檔案改變信任。

```powershell
.\publish.bat "C:\Users\swang\Documents\ProgramMigrationAnalyzer-CompanyKeys\authorization-public-key.pem"
.\publish-issuer.bat
```

客戶 EXE 輸出至 `publish/win-x64-single-file/ProgramMigrationAnalyzer.App.exe`，公司工具另輸出至 `publish/company-license-issuer/ProgramMigrationAnalyzer.LicenseIssuer.exe`。兩者為 Windows x64 自包含單檔，內含 .NET runtime。只交付客戶 EXE，公司人員自行保管發證工具、加密私鑰、Key 與帳密。收件端無須 .NET SDK，未另測沒有任何 .NET 安裝的乾淨 VM。

發布先固定此次公鑰快照，再從正式來源建置，沒有公鑰、輸入不合法、使用 `--no-build` 或建置失敗都拒絕。成功後只替換 EXE，保留旁邊的 WebView2 資料。重新發布前請關閉執行中的舊 EXE。建置電腦仍需 .NET 10 SDK，腳本使用單一 MSBuild 節點並停用節點重用。

Markdown 的 HTML 預覽仍需要收件端安裝 Microsoft Edge WebView2 Runtime。若 WebView2 無法啟動，程式會改用純文字 Markdown 預覽。單檔 EXE 啟動時會在系統暫存目錄解開必要的原生程式庫。

## Run

```powershell
dotnet run --project .\src\ProgramMigrationAnalyzer.App\ProgramMigrationAnalyzer.App.csproj
```

發佈版直接執行 `ProgramMigrationAnalyzer.App.exe`。本機授權缺失或無效時先顯示授權匯入，已有有效授權才顯示登入。成功登入後才建立分析主畫面，重新啟動仍須登入。開發 build 若未提供公鑰，會保持未授權。

## 公司簽發、啟用與登入

1. 公司人員開啟獨立發證工具，在私鑰保護密碼兩欄輸入一致的密碼，按「產生並儲存新金鑰」。加密私鑰存於 Git 專案之外，公鑰供客戶端發布。私鑰檔拒絕覆寫，ACL 僅目前 Windows 使用者與 SYSTEM 可存取。
2. 選「建立帳號」，輸入帳號、顯示名稱與兩欄一致的登入密碼，按「更新帳號清單」，再按「簽發／重新簽發 Key」儲存 `.pma-key`。私鑰保護密碼與登入密碼分開輸入，送出後欄位清空，以狀態提示確認結果。
3. 使用上述公鑰發布客戶端，開啟客戶 EXE，選公司 Key，確認摘要後按匯入，手動完成 Windows UAC，再於提升的匯入視窗確認並匯入。成功後返回登入，仍須正確帳密。

同一 Key 可以供內部人員跨主機使用，不綁定電腦、不設定到期日，也不連線公司服務。客戶端不提供自行建帳或重設密碼入口，舊 `--configure-local-account` 參數已拒絕。

帳號為 3～64 個 ASCII 字元，允許英文字母、數字、點、底線及連字號，會去除前後空白並轉為小寫。新建與重設密碼接受 8～128 個 Unicode scalar values，允許 `@`、`!`、`#` 等符號、空白及 Unicode，不要求特定字元組合，不去除空白、不改大小寫。密碼只在視窗的遮罩欄位輸入，沒有共用預設帳密，不使用命令列、環境變數或腳本傳入密碼。

更新帳號時，由公司人員載入加密私鑰，開啟原 Key，選「重設密碼」、「啟用帳號」或「停用帳號」，更新清單後重新簽發。客戶端在登入畫面的授權更新入口匯入新版。同一授權只接受較高版本，完全相同版本與內容可重複匯入。換成不同授權需在提升視窗確認目前授權 id，不能只依先前預覽放行。每次登入重新讀取並驗章，重設或停用於下次登入生效，既有 session 持續至登出或程序結束。

帳號檔固定為 `%ProgramData%\ProgramMigrationAnalyzer\auth\users.json`，與 EXE 或目前工作目錄無關。檔案保存完整公司簽章 envelope，包含帳號與獨立 salt 的 PBKDF2-HMAC-SHA256 雜湊，不保存明文密碼。客戶端每次登入驗證公司 ECDSA 簽章，正常登入不改寫檔案。匯入流程建立工具目錄、auth 目錄及帳號檔的受保護 ACL，SYSTEM 與 Administrators 可維護，Users 只讀，不修改 ProgramData 或磁碟根 ACL。

遷移舊版時，先保留受保護的 unsigned 帳號檔備份及舊版 EXE，再匯入公司簽發的新 Key。舊檔不自動轉換或沿用，沒有有效 Key 不能登入。若匯入失敗，原子替換流程保留原檔。必要時由授權的管理者關閉所有工具程序，還原受保護備份與相符的舊版 EXE，舊 unsigned 檔不能供新版登入。

公司須另外備份加密私鑰及 Key，私鑰保護密碼分開保管。私鑰遺失就不能對原授權重新簽發。換公司公鑰時須重新發布客戶 EXE，以新私鑰重新簽發 Key，再由管理者匯入，執行時不接受公鑰設定覆寫。新公鑰版本不會沿用舊公鑰簽章。

設定缺漏、簽章無效、損壞或 ACL 不安全會要求匯入有效 Key 或聯絡管理者，不會自動建帳或放行。錯誤密碼、未知帳號與停用帳號顯示相同失敗提示。連續 5 次失敗後，此程序暫停提交 30 秒並顯示倒數，重啟程序會重設節流。

登入後可在主畫面按「登出」。執行分析、轉譯、載入或儲存時，登出停用並提示等待工作完成。登出會清除 session、來源、報告、預覽及執行紀錄，保留已寫入 `output/` 的檔案，下次登入建立空白工作區。

session 存於程序記憶體，沒有自動登入、記住密碼、SSO、角色權限矩陣或閒置逾時。簽章限制正常客戶端接受的帳號資料，主機管理員仍能修改 EXE、擷取程序記憶體或還原整套舊狀態。離線 revision 只對目前安裝的授權比較，不能阻止管理員完整回滾或刪檔後重放有效舊 Key。不加密分析輸出，也不提供集中停權或帳號同步。

## Sample Data

提供 10 份可分析的範例：

- `samples/4gl/`：5 份 Informix 4GL 風格原始碼
- `samples/csharp-framework/`：5 份舊版 .NET Framework C# 原始碼

## Supported C# Analysis

目前可分析 namespace、型別、繼承、欄位、屬性、方法、參數、方法呼叫、物件建立、SQL 字串，以及 `ConfigurationManager`、`WebRequest`、`BinaryFormatter`、`System.Web`、ADO.NET、File I/O 與 COM 等相依性。

## Supported 4GL Syntax

第一階段以 Informix 4GL 風格語法為主，涵蓋 `MAIN`、`FUNCTION`、`DEFINE`、`CALL`、SQL、cursor、transaction、`DISPLAY`、`INPUT`、`REPORT` 與常見控制流程。

此 parser 可擴充，但不是完整的 compiler-grade parser。

## Translation Behavior

- 舊版 C# 會保留可辨識的 class 與 method，並針對 legacy API 加入現代化建議。
- 4GL 會依分析結果產生可讀的 C# scaffold。
- 無法可靠判定的型別或語意會標示 `TODO(Migration)`，不宣稱自動完成語意等價移植。
- 轉譯結果可依目標框架輸出 `.net8.cs` 或 `.net10.cs`。

## Generated Output

分析報告與轉譯草稿會寫入 `output/`。此目錄中的執行產物預設不納入版本控制。

為避免不同目錄的同名來源檔互相覆寫，輸出名稱使用來源檔名加上完整來源路徑的 16 位雜湊，例如 `CustomerQuery.4gl.a1b2c3d4e5f60708.analysis.md` 與 `CustomerQuery.4gl.a1b2c3d4e5f60708.net10.cs`。同一路徑重新分析會更新同一組輸出檔。

## Validation

登入檢查可在 Solution 建置後執行：

```powershell
dotnet run --no-build --project .\tests\ProgramMigrationAnalyzer.AuthenticationChecks -- --suite all
```

2026-10-01 Phase 5 的 AuthenticationChecks 通過 80 組，涵蓋簽章、signed store、發證、啟用、發布、密碼、ACL、登入、啟動與操作門檻。T6 的 restore、build、Phase2Checks、RegressionChecks、WpfChecks 全部 exit 0，build 0 warnings／0 errors。WPF 檢查實際走過 WebView2 失敗時的純文字備援，正式公司 Key 的簽章／ACL 與客戶 EXE 發布已核對。使用者確認正式 EXE 的 Axeen 登入、Scenario A／B／C、HTML／純文字、忙碌登出、重登清空與輸出保留通過，證據分列於[離線授權交付紀錄](docs/Offline_Authorization_Verification_and_Delivery.md)。舊版 Phase 4 的 56 組及桌面結果保留為歷史證據，不套用到新版。

目前已驗證：

- `dotnet restore ProgramMigrationAnalyzer.sln` 成功
- `dotnet build ProgramMigrationAnalyzer.sln` 成功，0 warning、0 error
- 5 份 4GL 與 5 份 legacy C# samples 均可完成 parser 與 analyzer 流程
- `CustomerQuery.4gl` 可產生 Markdown 與 `.NET 10` C# scaffold
- `LegacyApiClient.cs` 可偵測 WebRequest 與 XML 相依性
- 產生的 `CustomerQuery.4gl.net10.cs` 可獨立編譯
- WPF 應用程式程序可啟動並保持正常回應

第二階段專項檢查可執行：

```powershell
dotnet run --project .\tests\ProgramMigrationAnalyzer.Phase2Checks\ProgramMigrationAnalyzer.Phase2Checks.csproj
```

檢查涵蓋資料夾排除、同名輸出、背景分析、切換文件期間的結果歸屬、同路徑重新載入、失敗後狀態，以及 WebView2 提示與 Log。第三階段另以真實 WPF 視窗檢查純文字備援，並以主程式實際操作確認 HTML 成功渲染。

第三階段的樣本與 WPF 畫面流程檢查：

```powershell
dotnet run --no-build --project .\tests\ProgramMigrationAnalyzer.RegressionChecks\ProgramMigrationAnalyzer.RegressionChecks.csproj
dotnet run --no-build --project .\tests\ProgramMigrationAnalyzer.WpfChecks\ProgramMigrationAnalyzer.WpfChecks.csproj
```

回歸檢查涵蓋 10 份樣本、規格 Scenario A/B/C、`.NET 8` 與 `.NET 10` 輸出、5 份 4GL 草稿的 C# 編譯，以及邊界輸入。WPF 檢查會顯示真實視窗並驗證畫面綁定、操作命令、頁籤、報告儲存與失敗提示。另以實際桌面操作確認原生對話框、文件切換、Markdown HTML 與 SQL 表格的可讀性。詳細結果和待確認項目見 [`docs/Phase3_Verification_and_Delivery.md`](docs/Phase3_Verification_and_Delivery.md)。

## Known Limitations

- 不提供完整 4GL compiler 或 production-grade source-to-source compiler。
- 不保證自動轉譯後的程式與來源程式完全語意等價。
- Migration Score 是規則式評估指標，不代表實際移植工時或成功率。
- SQL 與 legacy API 偵測結果仍需人工審查。
- WebView2 無法初始化時會提示並記錄錯誤，再降級為純 Markdown 文字預覽。

## Future Work

- AI semantic analysis
- RAG and vector search
- Additional 4GL dialects
- Automated compilation and unit-test generation
- Database migration analysis
- More advanced diff and validation workflows

## Documentation

完整需求與驗收條件請參閱 [`docs/ProgramMigrationAnalyzer_Codex_Spec.md`](docs/ProgramMigrationAnalyzer_Codex_Spec.md)。

目前離線授權 O1～O12 與交付狀態見 [`docs/Offline_Authorization_Verification_and_Delivery.md`](docs/Offline_Authorization_Verification_and_Delivery.md)，執行進度見[Phase 5 計畫](docs/superpowers/plans/2026-10-01-offline-signed-authorization-implementation-plan.md)。本機登入 L1～L14 的歷史結果另見 [`docs/Login_Verification_and_Delivery.md`](docs/Login_Verification_and_Delivery.md)。

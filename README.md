# Program Migration Analyzer

程式移植分析儀是一套 Windows WPF 工具，目標是分析舊版 .NET Framework C# 與 Informix 4GL 原始碼，產生結構化分析、Markdown 報告與現代化 C# 程式碼草稿。

> 目前狀態：第一階段功能與第二階段可靠性修正已合併。第三階段的樣本回歸、WPF 畫面流程及實際桌面操作已通過，WebView2 HTML 預覽與失敗備援均已驗證。按鈕與頁籤的視覺設計仍待使用者確認。

本機登入已接入正常啟動流程。登入 Phase 4 已完成回歸及 SWANG-PC 發佈部署驗證，新增／重設密碼最低 8 個字元，允許特殊符號。交付證據與未執行項目見 [登入驗證與交付紀錄](docs/Login_Verification_and_Delivery.md)，合併狀態以[實作計畫](docs/superpowers/plans/2026-10-01-local-login-implementation-plan.md)為準。

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
- 本機帳號登入、使用者顯示、登出與獨立管理入口

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
│  └─ ProgramMigrationAnalyzer.Infrastructure/
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

以下為建置環境需求。發佈 EXE 的收件端需 Windows x64，首次使用前由 Windows 管理者設定本機帳號，HTML 預覽另需 WebView2 Runtime。

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

在專案根目錄執行 `publish.bat`，會發佈 Windows x64 自包含版本至 `publish/win-x64-single-file/ProgramMigrationAnalyzer.App.exe`，內含 .NET runtime。收件端先複製 EXE，再依下方操作設定自己的本機帳號，無須安裝 .NET SDK 或另附 runtime。SWANG-PC 已確認啟動使用內含 runtime，未另測沒有任何 .NET 安裝的乾淨 VM。建置電腦仍需 .NET 10 SDK。批次檔使用單一 MSBuild 節點並停用節點重用。重新發佈前請先關閉正在執行的舊版 EXE。

Markdown 的 HTML 預覽仍需要收件端安裝 Microsoft Edge WebView2 Runtime。若 WebView2 無法啟動，程式會改用純文字 Markdown 預覽。單檔 EXE 啟動時會在系統暫存目錄解開必要的原生程式庫。

## Run

```powershell
dotnet run --project .\src\ProgramMigrationAnalyzer.App\ProgramMigrationAnalyzer.App.csproj
```

發佈版直接執行 `ProgramMigrationAnalyzer.App.exe`，不帶參數時先顯示登入視窗。成功後才建立分析主畫面，重新啟動仍須登入。

## 本機帳號設定與登入

首次部署由 Windows 管理者開啟「以系統管理員身分執行」的 PowerShell，切換到 EXE 所在目錄後執行：

```powershell
.\ProgramMigrationAnalyzer.App.exe --configure-local-account
```

1. 在「本機帳號設定」選擇「建立帳號」，輸入帳號、顯示名稱及兩欄一致的密碼。
2. 按儲存，確認提示「帳號設定已儲存，下次登入生效」。送出後密碼欄會清空，請以狀態提示確認結果。
3. 關閉管理視窗，使用一般權限、不帶參數重新開啟 EXE，以剛建立的帳密登入。

帳號為 3～64 個 ASCII 字元，允許英文字母、數字、點、底線及連字號，會去除前後空白並轉為小寫。新建與重設密碼接受 8～128 個 Unicode scalar values，允許 `@`、`!`、`#` 等符號、空白及 Unicode，不要求特定字元組合，不去除空白、不改大小寫。密碼只在視窗的遮罩欄位輸入，沒有共用預設帳密，不使用命令列、環境變數或腳本傳入密碼。

管理入口不會自動提升權限，也不會登入分析工具。一般權限開啟時無法編輯或儲存。已提升權限的管理者可選「重設密碼」、「啟用帳號」或「停用帳號」。建立／重設須提供顯示名稱與兩欄密碼，啟用／停用不更動密碼。每次登入都重新讀取帳號資料，因此重設或停用於下次登入生效，既有 session 會持續至登出或程序結束，需要立即停權時須結束現有程序。

帳號檔固定為 `%ProgramData%\ProgramMigrationAnalyzer\auth\users.json`，與 EXE 或目前工作目錄無關。管理模式建立工具目錄、auth 目錄與帳號檔的受保護 ACL，SYSTEM 與 Administrators 可維護，一般 Users 只能讀取。密碼以獨立 salt 的 PBKDF2-HMAC-SHA256 雜湊保存，正常登入不改寫帳號檔。請透過管理視窗維護，部署包不包含帳號資料，每台收件電腦須各自建帳。

設定缺漏、損壞或 ACL 不安全會提示聯絡管理者，不會自動建帳或放行。錯誤密碼、未知帳號與停用帳號顯示相同失敗提示。連續 5 次失敗後，此程序暫停提交 30 秒並顯示倒數，重啟程序會重設節流。

登入後可在主畫面按「登出」。執行分析、轉譯、載入或儲存時，登出停用並提示等待工作完成。登出會清除 session、來源、報告、預覽及執行紀錄，保留已寫入 `output/` 的檔案，下次登入建立空白工作區。

目前僅支援本機帳號，session 存於程序記憶體，沒有自動登入、記住密碼、SSO、角色權限矩陣或閒置逾時。本機登入保護桌面操作入口，不加密輸出、不抵抗 EXE 修改，也不提供集中停權或帳號同步。

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

2026-10-01 登入 Phase 4 修正版通過 56／56，涵蓋 contracts 5、crypto 8、store 13、admin 6、login 5、startup 13、access 6。另完成 SWANG-PC 正式 EXE 的建帳與 ACL、一般權限登入、Scenario A／B／C、HTML／純文字預覽、忙碌登出、重登清空、不同工作目錄及正常退出。各項方法與限制見[登入驗證與交付紀錄](docs/Login_Verification_and_Delivery.md)。

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

本機登入的 L1～L14 結果與發佈紀錄見 [`docs/Login_Verification_and_Delivery.md`](docs/Login_Verification_and_Delivery.md)。

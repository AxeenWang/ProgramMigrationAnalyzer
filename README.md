# Program Migration Analyzer POC

程式移植分析儀是一套 Windows WPF 概念驗證工具，目標是分析舊版 .NET Framework C# 與 Informix 4GL 原始碼，產生結構化分析、Markdown 報告與現代化 C# 程式碼草稿。

> 目前狀態：第一階段 POC 與第二階段可靠性修正已合併。第三階段的樣本回歸、WPF 畫面流程及實際桌面操作已通過，WebView2 HTML 預覽與失敗備援均已驗證。按鈕與頁籤的視覺設計仍待使用者確認。

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

在專案根目錄執行 `publish.bat`，會發佈 Windows x64 自包含版本至 `publish/win-x64-single-file/ProgramMigrationAnalyzer.App.exe`。將此 EXE 複製給 Windows x64 使用者即可，對方不需要安裝 .NET。建置電腦仍需 .NET 10 SDK。批次檔使用單一 MSBuild 節點並停用節點重用。重新發佈前請先關閉正在執行的舊版 EXE。

Markdown 的 HTML 預覽仍需要收件端安裝 Microsoft Edge WebView2 Runtime。若 WebView2 無法啟動，程式會改用純文字 Markdown 預覽。單檔 EXE 啟動時會在系統暫存目錄解開必要的原生程式庫。

## Run

```powershell
dotnet run --project .\src\ProgramMigrationAnalyzer.App\ProgramMigrationAnalyzer.App.csproj
```

## Sample Data

提供 10 份可分析的範例：

- `samples/4gl/`：5 份 Informix 4GL 風格原始碼
- `samples/csharp-framework/`：5 份舊版 .NET Framework C# 原始碼

## Supported C# Analysis

目前可分析 namespace、型別、繼承、欄位、屬性、方法、參數、方法呼叫、物件建立、SQL 字串，以及 `ConfigurationManager`、`WebRequest`、`BinaryFormatter`、`System.Web`、ADO.NET、File I/O 與 COM 等相依性。

## Supported 4GL Syntax

第一階段以 Informix 4GL 風格語法為主，涵蓋 `MAIN`、`FUNCTION`、`DEFINE`、`CALL`、SQL、cursor、transaction、`DISPLAY`、`INPUT`、`REPORT` 與常見控制流程。

此 parser 是可擴充的 POC parser，不是完整的 compiler-grade parser。

## Translation Behavior

- 舊版 C# 會保留可辨識的 class 與 method，並針對 legacy API 加入現代化建議。
- 4GL 會依分析結果產生可讀的 C# scaffold。
- 無法可靠判定的型別或語意會標示 `TODO(Migration)`，不宣稱自動完成語意等價移植。
- 轉譯結果可依目標框架輸出 `.net8.cs` 或 `.net10.cs`。

## Generated Output

分析報告與轉譯草稿會寫入 `output/`。此目錄中的執行產物預設不納入版本控制。

為避免不同目錄的同名來源檔互相覆寫，輸出名稱使用來源檔名加上完整來源路徑的 16 位雜湊，例如 `CustomerQuery.4gl.a1b2c3d4e5f60708.analysis.md` 與 `CustomerQuery.4gl.a1b2c3d4e5f60708.net10.cs`。同一路徑重新分析會更新同一組輸出檔。

## Validation

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
- Migration Score 是規則式 POC 指標，不代表實際移植工時或成功率。
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

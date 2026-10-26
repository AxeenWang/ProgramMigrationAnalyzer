# Program Migration Analyzer

程式移植分析儀是一套 Windows WPF 工具，用來分析舊版 .NET Framework C# 與 Informix 4GL 原始碼，產生結構化分析、Markdown 報告與 C# 程式碼草稿。

開啟程式即可操作，無須登入、建立帳號、匯入公司 Key 或連線公司服務。2026-10-26 已移除原有帳號、離線授權與公司發證工具。

## 功能

- 開啟單一原始碼或資料夾，辨識 `.cs`、`.4gl`、`.per`，支援 UTF-8 與 Big5
- 分析程式結構、函式呼叫、SQL、資料表與已知相依性
- 偵測舊版 .NET API 與移植問題，產生規則式評分
- 產生 Markdown 報告，提供 HTML 預覽及純文字備援
- 選擇 `.NET 8` 或 `.NET 10` 產生 C# 草稿，提供原碼並排檢視
- 顯示執行狀態及 Log，儲存報告與開啟輸出資料夾

資料夾載入會列出多份文件，分析與轉譯針對目前所選文件執行。

## 架構

```text
Source -> Parser -> AnalysisResult -> Analyzer -> Markdown Report / Source Translator
```

| 專案 | 責任 |
| --- | --- |
| `App` | WPF、MVVM、直接啟動主視窗、操作與文件生命週期 |
| `Core` | 共用模型與服務介面 |
| `Parsers` | Roslyn C# 解析與規則式 Informix 4GL 解析 |
| `Analysis` | 相依性、移植問題、標籤、評分與 Markdown |
| `Translation` | C# 移植提醒與 4GL 的 C# 骨架 |
| `Infrastructure` | 來源載入與輸出 |
| `build/` | Windows x64 自包含單檔發布 |

主視窗直接組裝各服務，不讀取帳號或授權檔。關閉主視窗時取消背景工作、釋放預覽與文件，保留已儲存的輸出。

## 建置與執行

建置端需要 Windows 與 .NET 10 SDK。採用 WPF、CommunityToolkit.Mvvm、Roslyn、AvalonEdit、Markdig 與 WebView2。

從正式專案根目錄執行：

```powershell
dotnet restore ProgramMigrationAnalyzer.sln
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet build ProgramMigrationAnalyzer.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --no-build --project src/ProgramMigrationAnalyzer.App
```

執行後直接顯示分析主畫面，按「開啟檔案」或「開啟資料夾」即可開始。

## 發布與部署

```powershell
.\publish.bat
```

輸出為 `publish/win-x64-single-file/ProgramMigrationAnalyzer.App.exe`，是 Windows x64 自包含單檔。將該 EXE 放到客戶環境即可開啟使用，不需要 .NET SDK、另行部署 .NET runtime、公司 Key、公鑰或帳密。

發布從正式來源重新建置，成功後才替換 EXE，失敗保留原 EXE。保留旁邊的 WebView2 資料，重新發布前請關閉執行中的舊 EXE。單檔啟動時由 .NET 在系統暫存目錄解開必要的原生程式庫。

HTML 預覽需要 Microsoft Edge WebView2 Runtime，初始化失敗時會提示並改用純文字 Markdown。不自動清理舊版已安裝的帳號、公司金鑰或過往發證工具，新版不使用這些檔案。正式交付只取本次重新發布的分析儀 EXE。

## 輸出與範例

報告與草稿預設寫入專案的 `output/`。獨立部署時寫入目前工作目錄下的 `output/`，執行位置需允許寫入。輸出檔名包含來源檔名及完整來源路徑的 16 位雜湊，避免不同目錄的同名檔互相覆寫，同一路徑重跑則更新原輸出。

`samples/4gl/` 與 `samples/csharp-framework/` 各提供 5 份範例。執行產物不納入版本控制。

## 驗證

Solution 建置後執行：

```powershell
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.Phase2Checks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.RegressionChecks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.WpfChecks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.PublishChecks
```

- Phase2Checks：掃描排除、同名輸出、背景分析、文件切換、重新載入、失敗狀態與預覽錯誤提示
- RegressionChecks：10 份樣本、Scenario A/B/C、.NET 8/10、4GL 草稿編譯與邊界輸入
- WpfChecks：真實 WPF 直接啟動、可操作命令、畫面綁定、分析轉譯、預覽、儲存、關閉釋放及輸出保留
- PublishChecks：無 Key 發布、替換 EXE、保留 WebView2 資料，以及失敗或異常輸出時保留原 EXE

WpfChecks 會顯示真實視窗。PublishChecks 使用隔離建置替身，不能取代實際 SDK 發布與 EXE 啟動驗證。

## 已知限制

- C# 為語法層級分析，沒有整個 Solution 的跨檔語意解析
- 4GL 與 SQL 解析使用規則，並非完整 compiler-grade parser
- C# 轉譯保留原碼並插入提醒，4GL 產生待人工補完的骨架，不保證語意等價
- 型別、SQL、交易、UI 與商業邏輯仍需人工審查，評分不代表實際工時或成功率
- Diff 為原碼與結果並排檢視，沒有逐行差異標示

完整需求見 [專案規格](docs/ProgramMigrationAnalyzer_Codex_Spec.md)，本次結果見 [直接啟動驗證與交付紀錄](docs/Direct_Startup_Verification_and_Delivery.md)。舊登入及授權文件已標示為歷史資料，不作為現行部署要求。

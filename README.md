# Program Migration Analyzer

程式移植分析儀是一套 Windows WPF 工具，用來分析舊版 .NET Framework C# 與 Informix 4GL 原始碼，產生結構化分析、Markdown 報告與 C# 程式碼草稿。

程式採用完全離線的帳密登入，驗證成功後才開啟分析主畫面。不需要網路、公司 Key 或額外的客戶端帳號檔，也不提供自行建帳或重設密碼。

## 功能

- 主視窗與登入共用深藍自訂標題列，顯示程式名稱與組件版號，保留系統視窗操作
- 專屬程式圖示套用至 EXE、視窗與標題列，檔案勾選工具使用圖形與圓角按鈕
- 開啟單一原始碼或資料夾，辨識 `.cs`、`.4gl`、`.per`，支援 UTF-8 與 Big5
- 分析程式結構、函式呼叫、SQL、資料表與已知相依性
- 偵測舊版 .NET API 與移植問題，產生規則式評分
- 產生 Markdown 報告，提供 HTML 預覽及純文字備援
- 4GL 單檔離線分析保留完整 SQL、條件與命令路徑、變數資料流、註解證據及解析診斷，不需要資料庫或 LLM
- Markdown 以處理階段、商業規則與例外為正文，完整 SQL 各列一次。大型報告以本機 HTML 檔預覽，關閉視窗後清理該視窗的預覽檔
- 選擇 `.NET 8` 或 `.NET 10` 產生 C# 草稿，提供原碼並排檢視
- 顯示執行狀態及 Log，儲存報告與開啟輸出資料夾

資料夾載入會列出多份文件，分析與轉譯針對目前所選文件執行。

## 架構

```text
Source -> Parser -> AnalysisResult -> Analyzer -> Markdown Report / Source Translator
```

| 專案 | 責任 |
| --- | --- |
| `App` | WPF、MVVM、內嵌雜湊登入、操作與文件生命週期 |
| `Core` | 共用模型與服務介面 |
| `Parsers` | Roslyn C# 解析與 Informix 4GL 詞彙、結構及 SQL 證據解析 |
| `Analysis` | 相依性、移植問題、標籤、評分與 Markdown |
| `Translation` | C# 移植提醒與 4GL 的 C# 骨架 |
| `Infrastructure` | 來源載入與輸出 |
| `build/` | Windows x64 自包含單檔發布 |

啟動只建立登入視窗，成功後才組裝主視窗及分析服務。帳號、隨機 salt 與 PBKDF2-HMAC-SHA256 雜湊在建置時嵌入 App，不保存明文密碼，執行時不讀取外部帳號或授權設定。關閉登入視窗即退出，關閉主視窗時取消背景工作、釋放預覽與文件，保留已儲存的輸出。

## 建置與執行

建置端需要 Windows 與 .NET 10 SDK。採用 WPF、CommunityToolkit.Mvvm、Roslyn、AvalonEdit、Markdig 與 WebView2。

從正式專案根目錄執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Set-EmbeddedLogin.ps1
dotnet restore ProgramMigrationAnalyzer.sln
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet build ProgramMigrationAnalyzer.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --no-build --project src/ProgramMigrationAnalyzer.App
```

設定工具會互動要求帳號與兩次遮罩密碼，將驗證資料存入忽略追蹤的 `config/embedded-login.json`。不接受明文密碼參數，不把密碼寫入檔案或紀錄。缺少設定時建置失敗，沒有預設帳密或免登入備援。日後更換帳密時，使用設定工具的 `-Force` 重新產生資料，再重新建置及部署，留存的舊 EXE 不會因此失效。

每次啟動均須登入。帳號區分大小寫，密碼保留原樣與前後空白，不會記住或自動填入。登入後按「開啟檔案」或「開啟資料夾」即可開始。不接受跳過登入或舊版帳號管理參數。

## 發布與部署

```powershell
.\publish.bat
```

輸出為 `publish/win-x64-single-file/ProgramMigrationAnalyzer.App.exe`，是 Windows x64 自包含單檔。將該 EXE 放到客戶環境，輸入建置時設定的帳密即可使用，不需要 .NET SDK、另行部署 .NET runtime、公司 Key、公鑰或額外帳號檔。不要將建置端的 `config/embedded-login.json` 一併交付。

發布需要本機內嵌驗證設定，從正式來源重新建置，成功後才替換 EXE，缺設定或建置失敗保留原 EXE。保留旁邊的 WebView2 資料，重新發布前請關閉執行中的舊 EXE。單檔啟動時由 .NET 在系統暫存目錄解開必要的原生程式庫。

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
- RegressionChecks：10 份樣本、Scenario A/B/C、.NET 8/10、4GL 草稿編譯、完整 SQL 與條件資料流合成回歸、編碼與邊界輸入
- WpfChecks：真正的 PBKDF2、設定生成與缺設定建置、登入及取消、晚到結果、重複提交、可操作命令、畫面綁定、分析轉譯、預覽、儲存、關閉釋放及輸出保留
- PublishChecks：缺設定拒絕、無外部 Key 發布、替換 EXE、保留 WebView2 資料，以及失敗或異常輸出時保留原 EXE

WpfChecks 會顯示真實視窗。PublishChecks 使用隔離建置替身，不能取代實際 SDK 發布與 EXE 啟動驗證。

## 已知限制

- C# 為語法層級分析，沒有整個 Solution 的跨檔語意解析
- 4GL 採詞彙與結構解析及有界符號分析，保留未知與原文診斷，並非完整編譯器或跨檔語意分析
- C# 轉譯保留原碼並插入提醒，4GL 產生待人工補完的骨架，不保證語意等價
- 型別、SQL、交易、UI 與商業邏輯仍需人工審查，評分不代表實際工時或成功率
- Diff 為原碼與結果並排檢視，沒有逐行差異標示
- 離線登入不能抵抗掌握主機權限的刻意反編譯或修改，也不能遠端撤銷舊 EXE。混淆目前只有相容性評估，尚未套用

完整需求見 [專案規格](docs/ProgramMigrationAnalyzer_Codex_Spec.md)，功能與限制見 [4GL 單檔離線 Markdown 分析](docs/FourGl_Markdown_Analysis.md)，目前登入與建置流程見 [內嵌雜湊離線登入](docs/Embedded_Login.md)，視窗操作見 [自訂標題列](docs/Custom_Window_Caption.md)，另見 [混淆相容性評估](docs/Obfuscation_Compatibility.md)。先前直接啟動、本機帳號及 Key 授權文件均為歷史紀錄。

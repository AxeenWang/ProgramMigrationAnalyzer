# Phase 3 驗證與交付紀錄

更新日期：2026-09-23

## 範圍

Phase 3 涵蓋完整樣本回歸、WPF 畫面流程驗證與交付資訊。此文件區分自動化檢查、實際桌面操作與仍待使用者確認的視覺設計。

## 驗證結果

| 項目 | 結果 | 證據 |
| --- | --- | --- |
| Solution restore | 通過 | `dotnet restore ProgramMigrationAnalyzer.sln`，新增 WPF 檢查專案另以單一 MSBuild 節點完成 restore |
| Solution build | 通過，0 warning、0 error | 最終修正後以單一節點、停用節點重用與共用編譯程序完成建置 |
| 10 份樣本 | 通過 | `ProgramMigrationAnalyzer.RegressionChecks`，5 份 4GL 與 5 份 C# 均完成載入、分析、Markdown 與轉譯 |
| 4GL 輸出編譯 | 通過 | 5 份 4GL 樣本產生的 C# 草稿均通過 Roslyn 編譯檢查 |
| 規格 Scenario A/B/C | 通過 | CustomerQuery 分析、LegacyApiClient 風險辨識與 CustomerQuery `.NET 10` 轉譯 |
| 邊界輸入 | 通過 | 空白 4GL、語法錯誤 C#、Big5 編碼與不支援副檔名 |
| WPF 畫面流程 | 通過 | `ProgramMigrationAnalyzer.WpfChecks` 啟動真實視窗，驗證檔案與資料夾載入、原始碼與分析綁定、`.NET 8/10` 轉譯、Diff、Markdown 儲存、狀態與 Log |
| WebView2 失敗備援 | 通過 | 本機 WPF 檢查收到 `0x8000FFFF (E_UNEXPECTED)`，畫面切換為純文字 Markdown，並提供提示與 Log |
| 實際桌面操作 | 通過 | 以主程式及原生對話框開啟 4GL、C# 與 4GL 資料夾，完成分析、轉譯、Diff、文件切換、同路徑重新開啟和 Markdown 儲存 |
| WebView2 HTML 預覽 | 通過 | 主程式成功顯示 4GL 與 C# 報告。現場檢查標題、內文、行內程式碼、SQL 表格與提示區塊，均為淺色背景及可讀的深色文字 |

WPF 自動化檢查使用腳本化檔案對話服務驅動畫面命令。2026-09-23 另以實際主程式操作滑鼠、鍵盤與原生檔案對話框。自動化檢查的 WebView2 初始化失敗沒有在主程式重現，兩條路徑分別驗證成功渲染與失敗備援，不能由此判定測試宿主失敗的根因。

桌面操作中，`CustomerQuery.4gl` 分析得到 3 個函式、3 筆 SQL、3 個相依性與 86 分，並產生 `.NET 10` 草稿。`LegacyApiClient.cs` 得到 5 個函式、1 筆 SQL、5 個相依性與 80 分，選擇 `.NET 8` 後產生對應草稿。原生儲存對話框寫出 `output/CustomerQuery.manual-ui.md`，檔案內容已確認。載入 `samples/4gl/` 後畫面列出 5 個檔案，切換與重新開啟 `CustomerQuery.4gl` 時，舊分析與轉譯內容清除，`BatchSettlement.4gl` 的分析結果留在其自身文件。

## 本階段 UI 修正

- 頂部操作按鈕改為一致的圓角樣式，分析與轉譯採用清楚的主操作色彩。
- 預覽頁籤改為較清晰的選取與停留狀態。
- Markdown HTML 明確指定淺色配色、白色背景與深色文字，純文字備援也設定白底深字。
- 唯讀的 AvalonEdit 與分析欄位使用單向綁定。自動化畫面檢查曾找出空白原始碼預覽與分析失敗，修正後流程通過。
- WebView2 改為切換到 Markdown 頁籤時才嘗試初始化。

## 待使用者確認

由使用者確認按鈕與頁籤的視覺設計是否符合預期。技術流程已在實際桌面操作中通過，這項視覺偏好尚未獲得使用者確認。樣本報告沒有 fenced code block，因此沒有對該樣式進行畫面檢查。

Phase 3 的功能與互動式 UI 驗證已完成，視覺設計仍待使用者確認。

## 執行與輸出

- 安全建置：先設定 `MSBUILDDISABLENODEREUSE=1` 與 `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1`，再執行 `dotnet build ProgramMigrationAnalyzer.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`
- 主程式：`dotnet run --project .\src\ProgramMigrationAnalyzer.App\ProgramMigrationAnalyzer.App.csproj`
- 樣本：`samples/4gl/` 與 `samples/csharp-framework/`
- 分析與轉譯產物：`output/`，執行產物不納入版本控制
- 回歸檢查：`tests/ProgramMigrationAnalyzer.RegressionChecks/`
- WPF 畫面檢查：`tests/ProgramMigrationAnalyzer.WpfChecks/`

移植評分是規則式評估指標。4GL 轉譯為可讀的 C# 草稿，不能保證與來源程式語意等價。SQL、相依性與移植建議仍需人工審查。

# 內嵌雜湊離線登入實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在封閉客戶環境以內嵌雜湊驗證登入，保留原有分析功能。

**Architecture:** App 從內嵌資源建立驗證器，登入成功後才建立既有主視窗。本機建置設定只包含驗證資料，不作為客戶端執行時設定。

**Tech Stack:** .NET 10、WPF、System.Security.Cryptography、System.Text.Json、PowerShell，不增加套件。

**Spec:** `docs/superpowers/specs/2026-10-26-embedded-login-design.md`

## Global Constraints

- 使用正式專案根目錄，不使用復原 worktree。
- 不發布、不執行 Git 寫入，不安裝混淆工具。
- PBKDF2-HMAC-SHA256，600,000 次迭代，16 bytes salt，32 bytes 雜湊。
- 正式帳密及驗證資料不進入版本控制，測試使用合成資料。
- 固定正式檔案後才執行最終驗證。

## Review Focus

1. 取消或關閉後晚到的成功結果不得建立主視窗。
2. 空密碼、密碼前後空白與大小寫不得被默默變更。
3. 缺少或損壞設定不得自動放行，也不得讀取外部檔案補救。
4. 重複提交不得觸發多次成功或建立多個主視窗。
5. 發布缺少驗證資料時需失敗，並保留原 EXE，驗證程序不得改動現有發布產物。

## Task 1: 驗證器與本機建置設定

**Files:** `src/ProgramMigrationAnalyzer.App/Authentication/`、App csproj、`.gitignore`、`build/Set-EmbeddedLogin.ps1`、`tests/ProgramMigrationAnalyzer.WpfChecks/EmbeddedLoginChecks.cs`。

**Interfaces:** `ILoginVerifier.Verify(string username, string password)` 與 `EmbeddedLoginVerifier.FromStream(Stream stream)`，預設由 App assembly 的固定資源名稱讀取。

- [ ] 補登入啟動門檻失敗測試，記錄 RED。
- [ ] 補真實 PBKDF2 與設定損壞、輸入邊界的合成測試。
- [ ] 實作嚴格設定解析、密碼驗證及遮罩輸入設定工具，禁止明文密碼輸入介面。
- [ ] 加入忽略規則及建置必要資源檢查，產生本機正式驗證資料。

## Task 2: WPF 登入與既有功能整合

**Files:** `App.xaml.cs`、`LoginWindow.xaml` 與 code-behind、WpfChecks。

**Interfaces:** LoginWindow 消費 ILoginVerifier，只有驗證成功才通知 App 建立既有 MainWindow。

- [ ] 補登入錯誤、取消、重複提交與晚到結果測試。
- [ ] 實作背景驗證、統一失敗提示、密碼清除及視窗切換。
- [ ] 更新 WPF 原有測試，先經真實驗證流程後繼續原功能驗收。

## Task 3: 建置邊界、文件與固定版本驗證

**Files:** README、發布檢查、登入與混淆評估文件。

- [ ] 驗證缺設定建置失敗，設定工具只產生驗證資料，發布替身保留原 EXE。
- [ ] 更新目前使用與建置文件，將舊直接啟動文件標記為歷史。
- [ ] 評估混淆相容性，不安裝、不執行混淆或正式發布。
- [ ] 停止正式檔案寫入並記錄雜湊快照。
- [ ] 執行 Solution build、Phase2Checks、RegressionChecks、WpfChecks、PublishChecks。
- [ ] 以唯讀審查及雜湊比對確認成果與限制，不提交或推送。

使用者已確認設計並明確要求開始作業，不重複要求執行方式或計畫核准。依授權略過所有 commit、push 與分支步驟。

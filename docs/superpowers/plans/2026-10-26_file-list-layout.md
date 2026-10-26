# 檔案列表排版改善計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 避免控制項、Tags 與批次結果擠壓檔案列表，讓大量檔案可持續瀏覽。

**Architecture:** 保留既有命令、資料繫結與分析流程，只重排 WPF 容器。左側使用完整內容區高度，右側依序放預覽、評估與狀態／Log，左右用 GridSplitter 調整寬度。

**Tech Stack:** 現有 .NET 10、WPF、既有 WpfChecks，不新增套件。

**Spec:** 使用者已確認的建議：左側只保留勾選工具、數量與檔案列表，批次／取消／輸出設定移到上方共用工具列，Tags 移到分析結果，批次摘要及失敗明細整合到下方，加入左右拖曳分隔線。

## 方案

採搬移輔助資訊並保留左側高度的方案。只加寬左欄仍會被 Tags 的自動高度擠壓，僅收合 Tags 也不能保證大量檔案、錯誤明細或小視窗下的可用高度。保留原有五個預覽頁籤與所有命令，檔名欄隨左欄寬度延伸。

## Global Constraints

- Active Target: Projects/ProgramMigrationAnalyzer，Target Lock: enabled，CodeLab-managed。
- 從正式專案根目錄與 codex/file-list-layout 實作，不建立 worktree。
- 本次只做本機修改與驗證，不 commit、push、建立 PR、合併或發布。
- 不將使用者截圖或客戶內容寫入正式測試、程式或文件。
- 暫存、合成來源及擷取畫面使用 .camp_codex/2026-10-26_file-list-layout/。
- 不變更分析、報告、批次排程與輸出路徑語意。

## Review Focus

- 在 1440×920 與最小 1100×720 視窗，列表取得左側至少 75% 高度。
- 300 支檔案、120 個 Tags、200 筆失敗明細都不縮減左側列表高度。
- 拖曳分隔線後，檔名實際可用寬度增加，預覽維持最小可用寬度。
- 兩個視窗尺寸下，所有既有與批次操作按鈕都在可見範圍內。
- Tags 與失敗明細仍可查看，切換文件、選取及勾選語意不變。

## Task 1: 排版與真實 WPF 驗證

**Files:** src/ProgramMigrationAnalyzer.App/MainWindow.xaml、tests/ProgramMigrationAnalyzer.WpfChecks/Program.cs、docs/FourGl_Markdown_Analysis.md。

**Interfaces:** 既有 MainViewModel 命令與屬性不變。新增的純排版容器不加入業務邏輯。WpfChecks 透過 ItemsSource、視覺樹與實際尺寸驗證列表、Tags、失敗明細、按鈕及拖曳行為。

- [x] 加入合成大量檔案與 Tags 的 WPF 版面檢查，在舊版面上確認失敗。
- [x] 重排工具列、左側列表、右側 Tags 與下方診斷區，加入可拖曳分隔線並解除檔名固定寬度。
- [x] 驗證一般與最小尺寸、拖曳加寬、切換診斷頁籤，擷取合成資料畫面並檢視。
- [x] 更新操作文件，完成唯讀審查，停止寫入後執行完整 solution build、Phase2Checks、RegressionChecks、WpfChecks、PublishChecks，確認驗證期間正式檔案雜湊一致。

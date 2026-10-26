# 共用自訂標題列實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 主視窗與登入共用自訂標題列，保留 Windows 視窗行為及原有工作流程。

**Architecture:** WindowTitleBar 呈現標題與系統操作，CustomWindowFrame 設定 WindowChrome 並處理自訂最大化區域。共用 Window template 包住現有視窗內容。

**Tech Stack:** .NET 10 WPF、WindowChrome、SystemCommands、user32，不新增套件。

**Spec:** `docs/superpowers/specs/2026-10-26-custom-caption-design.md`

## Global Constraints

- 正式根目錄作業，使用現有主題分支，保留尚未提交的 1.0.1.0 修改。
- 不提交、推送、發布、安裝或讀取正式帳密內容。
- 系統 Title 與既有版本來源不變，主視窗可縮放，登入不可縮放。
- 最終驗證前停止正式檔案寫入。

## Review Focus

1. 最大化非客戶區命中與滑鼠按下／放開不得重複切換。
2. 取消、滑鼠移出及視窗關閉要釋放捕捉與 native hook。
3. 螢幕負座標與當前 DPI 的命中須與實際按鈕一致。
4. 登入的關閉鈕不得開啟工作區，晚到成功仍須忽略。
5. 最大化不遮蓋工作列，還原與既有最小視窗排版正常。

## Task 1: 共用標題列及 WindowChrome

**Files:** `Controls/WindowTitleBar.xaml` 與 code-behind、`Behaviors/CustomWindowFrame.cs`、`Resources/ApplicationResources.xaml`、WpfChecks。

**Interfaces:** `CustomWindowFrame.Attach(Window window)` 套用原生行為，template 的 `PART_TitleBar` 提供可命中的標題列，控制項的按鈕處理 SystemCommands。

- [ ] 新增共用標題列的失敗測試，確認 RED。
- [ ] 實作共用控制項、樣式及 native hook。
- [ ] 套用兩個視窗，保留 Title 與原有內容。

## Task 2: 視窗互動與整合驗證

**Files:** `tests/ProgramMigrationAnalyzer.WpfChecks/WindowChromeChecks.cs`、既有登入與視窗檢查。

- [ ] 驗證標題版本、按鈕可見性、原生命中及狀態切換。
- [ ] 驗證取消、滑鼠移出與晚到結果，透過自訂關閉鈕結束正式啟動路徑。
- [ ] 擷取登入與主視窗畫面，檢查最小尺寸與控制項位置。
- [ ] 完成唯讀審查後固定正式檔案，執行 Solution build 與四組回歸。
- [ ] 比對正式檔案、驗證設定及既有發布 EXE 的雜湊，回報實際驗證範圍。

使用者已確認對兩個視窗實作，接續目前工作，不重複要求執行方式確認。依本次授權略過 commit 與 push。

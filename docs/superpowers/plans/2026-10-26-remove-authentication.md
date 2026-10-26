# 移除登入與離線授權 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 恢復直接啟動即可操作，移除帳號與 Key 的部署需求。

**Architecture:** App 直接建立 MainWindow，MainViewModel 僅以工作生命週期及忙碌狀態控制操作。移除認證、發證及公鑰建置專案，保留既有分析架構與單檔發布。

**Tech Stack:** C#、.NET 10、WPF、CommunityToolkit.Mvvm、PowerShell。

**Spec:** `docs/superpowers/specs/2026-10-26-remove-authentication-design.md`

## Global Constraints

- 在正式專案根目錄與 `codex/remove-authentication` 分支實作。
- 不變更分析及轉譯規則，不新增依賴，不存取外部帳號或金鑰。
- 初次實作僅交付本機變更。後續 Git 操作依使用者當前授權執行，2026-10-26 已追加授權本次 commit 與 push，不建立 PR。

## Review Focus

- 無帳號檔、無公司公鑰時仍直接進入可操作的主視窗。
- 主視窗關閉時取消背景工作，不回寫已釋放的文件或 UI。
- 文件切換、重新載入、分析與轉譯失敗仍正確處理。
- 發布不再讀取 Key，失敗仍保留先前 EXE。
- 現行入口與文件沒有殘留登入或發證需求。

### Task 1: 恢復直接操作並移除認證功能

**Files:** App 啟動、MainWindowFactory、MainViewModel、MainWindow、Solution、認證與發證專案、發布腳本、Phase2Checks、WpfChecks、發布檢查與現行文件。

**Interfaces:** 保留 Core 的分析介面，MainWindowFactory.Create() 與 MainViewModel 不再接收 IUserSession。App 提供 protected virtual MainWindow CreateMainWindow() 供既有 WPF 測試注入檔案對話服務。

- [x] 修改 WPF 啟動斷言並執行，確認現行登入門檻使測試失敗。
- [x] 移除登入、授權與發證元件，簡化啟動、UI 與生命週期檢查。
- [x] 更新測試、發布腳本、README 與規格，標示歷史文件。
- [x] 執行完整建置、保留的檢查專案、無 Key 發布及啟動驗證。
- [x] 檢閱差異、殘留引用與 Git 狀態，交付本機變更及測試結果。

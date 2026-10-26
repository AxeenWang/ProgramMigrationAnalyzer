# 專屬圖示與勾選按鈕實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement task-by-task, with one isolated asset subtask.

**Goal:** 加入專屬圖示，改善兩個勾選操作的 WPF 外觀。

**Architecture:** DrawingImage 與多尺寸 ICO 共用視覺設計，ApplicationIcon 處理 EXE，Window style 與自訂標題列讀取共用圖示。按鈕 ContentTemplate 與 ControlTemplate 保留原有文字及命令。

**Tech Stack:** WPF、ICO、DrawingImage，不增加套件。

**Spec:** `docs/superpowers/specs/2026-10-26-app-icon-selection-design.md`

## Global Constraints

- 維持 1.0.1.0，保留版本、自訂標題列及登入變更。
- 圖示代理只寫 Assets 與本次暫存子目錄，其他檔案由主代理整合。
- 不讀取正式帳密或客戶資料，不安裝、發布或執行 Git 寫入。

## Review Focus

1. EXE 的 Win32 圖示與 WPF 視窗圖示均需真正使用新資產。
2. 自訂標題列圖示更換後仍保留原生系統選單命中區。
3. 兩個按鈕在窄側欄不裁切，鍵盤焦點及停用狀態可辨識。
4. 原有全選、取消與批次勾選數量行為不變。
5. 多尺寸 ICO 及資源不可增加額外客戶端交付檔案。

## Tasks

- [ ] 資產子任務產生原創 DrawingImage、16～256 像素 ICO 及 PNG 預覽。
- [ ] 主代理建立勾選按鈕樣式並整合圖示至 App、Window 及標題列。
- [ ] 檢查所有資產，停止代理寫入後驗證實際組件、視窗及既有操作。
- [ ] 固定正式檔案與設定的雜湊，執行完整建置與四組回歸，擷取畫面。
- [ ] 回報本機結果，不提交、推送或發布。

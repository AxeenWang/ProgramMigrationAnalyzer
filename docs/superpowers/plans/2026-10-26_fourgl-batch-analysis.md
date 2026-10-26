# 4GL 資料夾批次分析實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 選取資料夾後，全選其中含子資料夾的 .4gl，逐支使用既有單檔分析流程並各自產生 Markdown。

**Architecture:** 資料夾先掃描路徑，查看或分析時才讀取來源。單檔與批次共用分析方法，批次依序執行、逐檔隔離錯誤，僅為目前查看的報告建立預覽 HTML。

**Tech Stack:** 現有 .NET 10、WPF、CommunityToolkit.Mvvm、Markdig 與四組檢查程式，不新增套件。

**Spec:** 本文件下列已核准設計，依本討論串使用者同意的建議實作。

## 已核准設計與方案

採用檔案勾選加依序批次分析，保留單檔按鈕。使用者開啟資料夾後，其 .4gl 預設勾選，可全選或取消。點選檔案僅切換預覽，不改變勾選。其他格式保留既有單檔用途，不納入本次批次。

沿用遞迴掃描、既有排除目錄與不追蹤子目錄連結的規則。每支來源各寫一份 output 報告，沿用路徑雜湊命名與同來源重新分析更新報告的行為。每支讀取、解析或輸出失敗後繼續，完成時顯示成功、失敗、未處理與失敗明細。取消保留完成報告，不處理後續項目。批次不觸發轉譯。

比較方案：維持逐支手動分析無法解決操作負擔，多支平行分析會增加記憶體及 UI 更新複雜度。選擇共用單檔流程的依序排程，符合使用者要求。

## Global Constraints

- Active Target: Projects/ProgramMigrationAnalyzer，Target Lock: enabled，CodeLab-managed。
- 從正式專案根目錄修改與驗證，不使用舊復原 worktree。
- 不執行來源 SQL、RUN、Shell 或郵件，不使用網路、資料庫或 LLM 分析。
- 不讀取其他專案，不納入客戶來源與參考區路徑。
- 本次只授權本機修改與驗證，不提交、推送、發布、建立 PR 或合併。
- 暫存紀錄放在 .camp_codex/2026-10-26_batch-analysis/，不建立其他暫存工作區。

## Review Focus

- 壞編碼、刪除或無法讀取的單一來源不阻斷掃描與其餘分析。
- 不同子資料夾同名來源輸出互不覆蓋，批次報告與單檔一致。
- 取消與關閉發生在讀取、解析或輸出期間，保留既有完整輸出並忽略關閉後結果。
- 切換預覽、語言或目標不能改變已開始批次的來源集合或分析設定。
- 數百筆來源不預先讀取全部內容或建立全部 HTML，失敗明細不因 Log 上限消失。

## Task 1: 批次選取、掃描與共同分析流程

**Files:** Core/Contracts.cs、Core/Models.cs、Infrastructure/SourceFileService.cs、Infrastructure/OutputWriter.cs、App/ViewModels/MainViewModel.cs、App/ViewModels/MainViewModel.Batch.cs、App/ViewModels/SourceDocumentViewModel.cs、tests/ProgramMigrationAnalyzer.Phase2Checks。

**Interfaces:** 新增 ISourceFileService.ScanFolderAsync，回傳 IsContentLoaded=false 的 SourceDocument 清單。ViewModel 提供 IsChecked、SelectAllFourGlCommand、ClearBatchSelectionCommand、AnalyzeCheckedCommand、CancelBatchCommand、批次計數與失敗明細。單檔與批次共用 AnalyzeDocumentAsync。

- [x] 先加入行為檢查，確認目前缺少批次入口而失敗。
- [x] 實作掃描路徑、延後讀取、勾選、依序分析與每筆錯誤隔離。
- [x] 實作取消、固定批次設定與原子報告寫入，補輸出失敗與取消行為檢查。
- [x] 驗證同名不同路徑、單檔與批次相等、選取切換、關閉取消與數百筆資料。

## Task 2: WPF 操作與文件、固定版本驗證

**Files:** App/MainWindow.xaml、tests/ProgramMigrationAnalyzer.WpfChecks/Program.cs、docs/FourGl_Markdown_Analysis.md。

**Interfaces:** 綁定 Task 1 命令、勾選與進度狀態，既有單檔與五個預覽頁籤維持可用。

- [x] 先加入真實 WPF 勾選及批次按鈕行為檢查，確認缺少 UI 而失敗。
- [x] 增加全選、取消勾選、分析勾選、取消批次、進度與失敗明細。
- [x] 更新功能文件，不改變既有單檔報告規格。
- [x] 停止寫入後執行 solution build、Phase2Checks、RegressionChecks、WpfChecks、PublishChecks，檢視差異並確認正式檔案雜湊未在驗證期間改變。

## Task 3: EXE 旁預設輸出、自訂根目錄與來源資料夾分層

使用者追加要求：預設 `<EXE 所在目錄>/output/<選取資料夾名稱>/`，保留選取範圍內的相對子資料夾。使用者明確選擇提供「設定輸出目錄」按鈕。自訂根目錄供本視窗後續分析及轉譯使用，不移動舊報告。個別開啟且無資料夾脈絡的來源仍直接輸出到根目錄。根目錄不依工作目錄或 solution 位置改變，不自動退回其他位置。

**Files:** Services/MainWindowFactory.cs、Services/FileDialogService.cs、ViewModels/MainViewModel.Output.cs、ViewModels/MainViewModel.cs、ViewModels/MainViewModel.Batch.cs、ViewModels/SourceDocumentViewModel.cs、Infrastructure/OutputWriter.cs、Core/Contracts.cs、MainWindow.xaml、MainWindow.xaml.cs 與既有測試及功能文件。

**Interfaces:** IOutputWriter 增加 SetOutputDirectory 與 GetDocumentOutputDirectory，寫入方法加入選取來源根目錄脈絡。每個文件保存當時選取的根目錄，批次仍固定來源集合。IFileDialogService 增加 SelectOutputFolder。MainViewModel 提供 ConfigureOutputDirectoryCommand、CurrentOutputDirectory。預覽 session 隨輸出根目錄切換並清理本視窗舊預覽。

- [x] 預設路徑與資料夾分層行為測試在舊實作上失敗。
- [x] 實作根目錄設定、來源分層、安全路徑邊界及取消保護。
- [x] 驗證跨資料夾集合、自訂根目錄、取消選取、單檔／批次／轉譯共用位置與預覽清理。
- [x] 更新文件，唯讀審查後停止寫入並重跑固定版本完整建置與四組檢查。

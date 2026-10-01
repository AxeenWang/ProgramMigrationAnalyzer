# 登入作業正式工作區復原

日期：2026-10-01。Active Target：`Projects/ProgramMigrationAnalyzer`，Target Lock enabled。

使用者明確授權將過往登入作業完整移回 `D:/AxeenWorld/CodeLab/Projects/ProgramMigrationAnalyzer`。本目錄現在持有 `codex/offline-authorization-phase-5`，HEAD 與同名 origin upstream 均為 `52fcb926f41239aeeb18cb005dc0d5373c77aa69`，ahead／behind 0／0。治理模式為 CodeLab-managed，適用專案 AGENTS.md 與 CodeLab anchors。

## 已復原內容

- Phase 1～4 的登入契約、密碼雜湊、帳號設定、登入視窗、session、操作門檻、測試及正式文件，均由已合併 Git 歷史完整帶回根目錄的 src、tests 與 docs。
- Phase 5 的 T1 `5a791c6` 與 T2 `52fcb92` 保留原提交及遠端紀錄。13 個未提交 T3 檔案逐一核對 SHA-256 後轉入正式位置，T3 仍在實作中。
- 原目錄的 6 個未提交檔案保留復原副本。AGENTS.md、.gitignore 內容已在較新分支，publish.bat 僅換行差異。規格保留已完成登入與最低 8 字元規則，舊計畫原稿保留而正式文件採用較新進度。
- 使用者原有操作簡報仍在 docs/operation-guide，內容指紋相同，保持未追蹤，不混入目前程式提交。
- 573 個歷史登入、授權與發布測試檔案已保留並核對指紋。這些紀錄與 fixtures 屬暫存證據，保存在根目錄 .codex-tmp，不作為正式程式來源。

舊 worktree 已解除分支占用並停在同一 commit 的 detached HEAD，依使用者指示保留目錄作復原副本。正式 publish.bat 已移除對該目錄的 fallback，缺少正式登入來源時直接失敗。

## 復原驗證

在正式根目錄重新 restore、build 及執行全部 AuthenticationChecks，80 PASS，exit 0，build 0 warnings／0 errors。這 80 組包含 T3 已完成的 5 組服務檢查，不代表 T3 WPF 發證流程已完成。

發布路由檢查先觀察舊腳本在缺少正式來源時執行暫存 worktree，再確認修正後 exit 1 且完全沒有執行 fallback。未重新發佈或修改真實 ProgramData 帳號。

復原副本及逐檔清單位於 `.codex-tmp/2026-10-01_formal-workspace-recovery/`。後續只從正式專案根目錄實作、建置與發布，不預建未開始的 Phase 分支。

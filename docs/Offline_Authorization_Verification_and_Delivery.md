# 離線簽章授權驗證與交付紀錄

更新日期：2026-10-01。Active Target：`Projects/ProgramMigrationAnalyzer`，Target Lock enabled，CodeLab-managed。正式來源、建置及發布均使用 `D:/AxeenWorld/CodeLab/Projects/ProgramMigrationAnalyzer`。

Phase 5 的 T1～T6 已完成驗證及推送，唯一 [PR #11](https://github.com/AxeenWang/ProgramMigrationAnalyzer/pull/11) 已建立為 ready，目前 OPEN，等待人員審查與合併。正式公司金鑰、公司 Key 發布與帳號替換已完成，使用者已確認新版正式 EXE 的完整桌面驗收通過。

## Git 與來源

- 分支：`codex/offline-authorization-phase-5`，基底 `7513324`。
- T1：`5a791c6`，T2：`52fcb92`，T3：`888c8f0`，T4：`5455e16`，T5：`64fe9c1`。
- T6 驗證與交付提交：`3e1b1fd`，推送後工作區乾淨、upstream 0／0。PR base 為 `main`，head 為本 Phase 分支，`isDraft=false`，沒有自動合併。
- 舊暫存 worktree 是非作業用復原副本，不作為來源或發布 fallback。
- 使用者原有操作簡報未修改，依使用者指定僅在本機 `.git/info/exclude` 排除該檔案，不納入本 Phase PR。

## 自動化驗證

T6 初始 restore、Solution build、AuthenticationChecks all、Phase2Checks、RegressionChecks、WpfChecks 均 exit 0。Build 0 warnings／0 errors。以下是目前版本的認證檢查數，舊版 Phase 4 的 56 組只作為歷史證據。

設定正式公司公鑰後，重新執行 Solution build、AuthenticationChecks all 與其餘三組回歸，全部 exit 0，build 0 warnings／0 errors，Authentication 仍為 80 PASS。原 EmbeddedTrustCannotBeOverridden 檢查因假設開發 build 永遠沒有公鑰而出現 1 組 RED，修正為獨立讀取編譯資源，確認 runtime trust 與資源一致，且外部公鑰、環境變數及工作目錄不能新增或取代信任後 GREEN。這是測試適用兩種建置設定的修正，產品沒有增加 runtime override。

| Suite | PASS 數 |
| --- | ---: |
| signature | 9 |
| signed-store | 10 |
| issuer | 12 |
| activation | 9 |
| publish | 6 |
| contracts | 5 |
| crypto | 8 |
| acl | 2 |
| login | 5 |
| startup | 8 |
| access | 6 |
| 合計 | 80 |

Phase2Checks 通過資料夾掃描、輸出名稱、背景分析、文件切換、重新載入、失敗狀態及 WebView2 提示。RegressionChecks 通過全部 5 份 4GL、5 份 C#、Scenario A／B／C、兩種目標框架、草稿編譯與邊界輸入。WpfChecks 通過登入門檻、使用者／登出綁定、空白重登工作區、保留輸出、開檔／資料夾、分析、轉譯、Diff、儲存及純文字報告備援。

本次 WPF 的 WebView2 初始化實際回報 `0x8000FFFF`，程式成功降級為純文字。自動化證據只涵蓋備援，正式 HTML 與純文字預覽通過的證據來自下列使用者桌面驗收。測試私鑰只在記憶體即時產生，隔離資料與 log 留在本專案忽略的 task temp。

## 使用者桌面驗收

使用者於本討論串明確確認「已完成以上全部桌面驗收」，問題指定本次新版正式 EXE，涵蓋 Axeen 登入、Scenario A／B／C 分析與轉譯、HTML／純文字預覽、忙碌時禁止登出，以及登出重登後工作區清空且輸出保留。這些結果記為使用者觀察，代理沒有以桌面操作工具重做或獨立觀察這些畫面。

使用者也已回覆啟用取消及全部視窗關閉。初次唯讀排查仍看到先前啟動的客戶端及 issuer 各一個程序，使用者授權結束這兩個程序後，執行前再次核對時兩者已退出，因此未執行強制終止。最終產品程序數為 0。

正式 EXE 以舊 `--configure-local-account` 啟動，使用者確認只有「程式無法啟動，請重新開啟或聯絡管理者」提示，沒有帳號設定或分析畫面，並關閉提示。獨立記錄的退出碼為預期的 1，帳號檔全檔 SHA256 未變。正常登入、主畫面與啟用取消的生命週期另由 startup／activation suites 覆蓋。

## O1～O12 覆蓋

| 驗收 | 自動化與檢查方法 | 正式部署狀態 |
| --- | --- | --- |
| O1 | activation 的無檔／unsigned 啟動與取消，正式 App 啟動路徑 | 使用者確認啟用取消，正式 Key 安裝及登入通過，最終相關程序數為 0 |
| O2 | 真實 ECDSA、signed provider 與 PBKDF2，登入成功及統一失敗提示 | 使用者確認正式公司 Key 的 Axeen 登入、分析與轉譯通過 |
| O3 | 所有受保護欄位、payload bytes、keyId 與 signature 篡改拒絕 | 拒絕案例留在隔離資料 |
| O4 | 非受信任金鑰、演算法、產品、schema 與非法結構拒絕 | 不破壞真實 Key |
| O5 | 持續增長 stream、4 MiB、深度、UTF-8／Base64、重複／未知欄位、帳號與 hash 邊界 | 已由隔離驗證覆蓋 |
| O6 | 原子匯入、writer lock、失敗 rollback、取消、一般權限、ACL 與 reparse 邊界，UAC launcher 替身 | 受保護備份、實際帳號替換與生產 ACL 驗證通過，產品 UAC 與啟用操作由使用者執行，最終相關程序數為 0 |
| O7 | 每次重登重新驗章、檔案篡改拒絕及舊管理參數拒絕 | 使用者確認正式登出重登及舊入口拒絕，舊入口 exit 1 且帳號檔未變 |
| O8 | 公司發證新建、重設、停用、啟用及 8 字元符號密碼 | 使用者已簽發 Axeen，正式 Key 驗章通過，使用者確認登入與完整桌面驗收通過 |
| O9 | 記憶體加密私鑰 roundtrip、錯誤保護密碼拒絕、公私鑰匹配、敏感輸入清除與受保護 writer | 使用者確認金鑰已儲存，私鑰 ACL 為目前使用者／SYSTEM 且停用繼承，代理未讀私鑰 |
| O10 | 同一 Key 在兩個隔離部署、revision／authorizationId／鎖內確認規則 | 不額外要求第二台真實主機 |
| O11 | 無公鑰／NoBuild 拒絕、來源快照、原子發布、兩工具隔離與實際 SDK bundle 核對 | 正式公司公鑰發布與 bundle 核對通過 |
| O12 | 所有 suites、樣本與 WPF 回歸 | 使用者確認 Scenario A／B／C、正式 HTML／純文字、忙碌登出、重登／輸出保留通過，視窗關閉後最終相關程序數為 0 |

計畫五項 Review Focus 分別由 GrowingStreamIsBounded、ReplacementConfirmationUsesLockedCurrentId／CandidateChangedBeforeElevation、CanceledElevationAndLateCompletion、IndependentSecretsAndEightCharacterSymbols、RootPublishAndPublicKeySnapshot 覆蓋。驗章與登入案例使用真實密碼學，ACL／UAC 替身只替換部署邊界，不取代正式機器驗收。

## 發布與自審

T5 實際 SDK 10.0.401 已在專案內隔離輸出發布兩個自包含單檔。獨立讀取 bundle manifest 及 App PE resource，確認內嵌公鑰與指定測試公鑰一致，App assembly 與正式 root build 的 SHA256 一致，客戶 bundle 沒有 issuer、私鑰、Key 或 users.json。詳見 Phase 5 計畫 T5 紀錄，這不是公司正式金鑰部署。

T6 已將公司發證工具發布到正式 `publish/company-license-issuer/`，使用者互動建立公司金鑰與 Axeen Key。公開公鑰快照已保存於本機忽略的 `config/authorization-public-key.pem`，直接執行正式根目錄 `publish.bat` 成功，客戶 EXE 已更新。正式客戶 bundle 的公鑰 keyId 為 `DF0EAF2EA197F530C20EF4890C6697153022E6B711F50489E14EBBFA4E1CBADA`，與公司公鑰相符，App assembly 與正式 root 的 Release build 指紋相符，無 issuer、PEM、Key 或 users.json 交付項目。客戶 EXE SHA256 為 `E49A68BA295B5E579B6CEEB05323A8616BA56318FF86261678D0DABA78CB56CD`。

T6 作者自審檢查 App 的 project references 與 trust resource、issuer 的敏感輸入生命週期、嚴格 codec／verifier、signed store 的 revision／鎖定／rollback、UAC launcher 與發布快照。App 不引用 issuer，執行時只從編譯資源取得公鑰，舊建帳／管理服務及模式已移除。Git 未追蹤實際金鑰、帳號檔、temp 或發布產物。使用者已指定不使用代理分工，獨立人員審查留給 PR reviewer。

自審發現客戶選檔篩選漏掉公司輸出的 `.pma-key`，已補入。Git ignore 同步補入 `.pma-key` 與公司預設私鑰檔名規則。這兩項為可逆的小幅修正，新版 Solution build 為 0 warnings／0 errors，activation 9 組通過，實際公司 Key 已安裝並核對。不新增只比對實作文字的測試。受限執行中的原子替換案例回報 StorageUnavailable，同一隔離 suite 在允許環境通過，沒有為測試讀寫真實 ProgramData。

## 已授權的真實部署

使用者於本討論串明確核准以下範圍。

| 範圍 | 授權與目前狀態 |
| --- | --- |
| 公司金鑰目錄 | `C:/Users/swang/Documents/ProgramMigrationAnalyzer-CompanyKeys`，使用者已保存 company-signing-private-key.pem 及 authorization-public-key.pem，私鑰 ACL 核對通過，另已簽發 internal-authorization.pma-key |
| 部署主機 | `SWANG-PC` |
| 舊帳號備份／替換 | `C:/ProgramData/ProgramMigrationAnalyzer/auth/users.json` 已換成公司 Key，與簽發檔的全檔 SHA256 相符，受保護舊版備份保留 |
| 部署 ACL | 允許維護工具目錄、auth 目錄與帳號檔，禁止改變 ProgramData 或磁碟根 ACL |

已保存部署前 ACL 的 metadata。唯讀檢查使用正式 WindowsLocalAccountAccessPolicy 與 SignedAuthorizationVerifier，確認安裝檔 ACL 及公司 ECDSA 簽章通過，revision 1，唯一帳號 axeen 已啟用。工具及 auth 目錄和帳號檔符合受保護 ACL，ProgramData 與磁碟根 SDDL 未變。代理不讀私鑰、不取得或輸出任何實際密碼，帳號檔不列印 hash／salt。

使用者手動完成 Windows UAC 後，舊帳號已備份為 `C:/ProgramData/ProgramMigrationAnalyzer/auth/users.pre-signed-20261001-0e624273b60646f6bc42e37e9c7e2bfe.backup`，受保護 ACL 只有 Administrators／SYSTEM FullControl。備份 SHA256 與備份當時的原檔一致。舊 EXE 恢復副本為本專案 task temp 的 `client-before-company-key-6907e4bf2f314a069115031f8a9c40ac.exe.backup`，指紋與發布前的正式舊 EXE 相符。ProgramData 與磁碟根 SDDL 比對均未變。

第一次備份 helper 因 PowerShell `Get-Item` 未加 `-Force` 而無法讀取隱藏的 ProgramData 目錄，在任何備份寫入前停止。重現後修正目錄 metadata 讀取，第二次手動 UAC 執行成功。這是部署 helper 修正，未修改產品 ACL policy 或降低 reparse 檢查。

## 恢復與交接界線

替換前須保留舊帳號檔受保護備份與舊 EXE。還原舊 unsigned 備份只適用相符舊版 EXE，新版不接受。所有恢復操作由授權的管理者在工具程序全部關閉後執行，不將備份、Key 或私鑰提交 Git。

公司應備份加密私鑰，保護密碼另行保管。丟失私鑰無法對舊授權重新簽發。更換公鑰須重新發布 EXE 並重新簽發／匯入相符 Key，執行時沒有信任根覆寫。

完全離線且客戶持有管理員權限，不能保證抵抗 EXE 修改、程序記憶體擷取或完整狀態回滾。revision 比較防止正常更新流程降版，不能阻止刪除帳號檔後重放有效舊 Key。既有 session 不即時撤銷，不加密分析輸出，不提供集中停權。

T6 的必要驗證、提交／推送與唯一 ready PR 已完成。公司金鑰、簽發 Key、正式客戶端發布、帳號替換、簽章及 ACL 核對已完成，完整桌面驗收已由使用者確認，最終相關程序數為 0。後續先審查 PR，合併後再依使用者指示同步主線及清理 Phase 分支。乾淨無 .NET 的 VM、第二台真實主機與管理員攻擊測試不在本次正式驗收。

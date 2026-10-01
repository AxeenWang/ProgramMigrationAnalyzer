# ProgramMigrationAnalyzer 離線簽章授權設計

日期：2026-10-01。狀態：規格與實作計畫均已核准，Phase 5 已開工，尚未完成客戶端接入或部署。

Active Target：`Projects/ProgramMigrationAnalyzer`。Target Lock：enabled。

## 1. 目的、基線與已確認需求

工具部署在客戶 VPN 內的 Windows 開發主機，完全離線。使用者希望只有持有公司內部帳密的人員能透過正式程式登入，客戶不能利用 Windows 管理員權限自行建立可用帳號。

使用者已確認採公司簽發的授權 Key，首次開啟匯入後建立本機登入資料。Key 包含帳號及密碼驗證資料，密碼不存明文，無須加密整份帳號檔。2026-10-01 另確認第一版不綁定電腦，同一 Key 可供內部人員跨主機使用。

既有密碼規則維持 8～128 個 Unicode scalar values，允許 `@`、`!`、`#`、空白與 Unicode，密碼原樣比較。帳號維持 3～64 個 ASCII 英文字母、數字、點、底線與連字號，Trim 後以 invariant 小寫正規化。

續作 Git root 為 `D:/AxeenWorld/CodeLab/Projects/ProgramMigrationAnalyzer/.codex-tmp/2026-10-01_login-phase-1/worktree`，採 standalone 模式與自己的 `AGENTS.md`。開工已 fetch，`main` 與 `origin/main` 為 `751332489dde5190b59a836774565ab1d458c634`，ahead／behind 為 0／0，tracked／untracked 工作區乾淨。

本機登入 Phase 1～4 與 publish 修正已合併。現行 `LocalAccountAdministrationService` 只檢查 Windows 提升權限，`--configure-local-account` 可以建立或重設帳號。這項行為須由新版本的公司簽章授權取代。

第一版採無到期日的離線授權，不新增裝置指紋、連線認證、角色矩陣或硬體鎖。無到期日是本草稿的工程選擇，納入本次規格審閱。

## 2. 選定方案與保護界線

| 方案 | 評估 |
| --- | --- |
| Windows 管理員維護本機帳號 | 現行作法，客戶持有管理員權限，無法區分公司發證權限 |
| 公司簽章授權＋本機帳密 | 已選定，適合完全離線，公司持有私鑰，客戶端只有驗證公鑰 |
| 連線身分驗證或硬體鎖 | 本次不採用，連線不符合環境，硬體鎖需要另訂部署與採購範圍 |

簽章保護帳號資料的來源與完整性。正常、未修改的客戶端只能使用公司簽發的帳號資料，Windows 管理員無法自行簽發、修改密碼雜湊、新增帳號或切換啟用狀態。

客戶管理員仍能修改 EXE、觀察程序或替換完整環境。本方案不宣稱能阻止主機所有者破解客戶端。Key 內的 salt／hash 可讀，持有者仍可能離線猜測密碼。最低 8 字元符合使用者要求，並不保證弱密碼安全。

Key 可跨主機複製。持有 Key 仍須知道密碼才能登入。完全離線且主機由客戶管理，無法可靠地集中停權或防止刪除狀態後重放舊 Key。

## 3. 交付與公司操作流程

交付分為兩個獨立 WPF 程式。

| 程式 | 對象與職責 |
| --- | --- |
| `ProgramMigrationAnalyzer.App.exe` | 客戶主機使用，驗證公鑰、匯入授權、登入、分析、轉譯與登出 |
| `ProgramMigrationAnalyzer.LicenseIssuer.exe` | 公司內部使用，產生公司簽章金鑰、建立／重設／停用帳號、簽發 `.pmauth` 授權檔 |

公司首次使用發證工具時，互動選擇公司內部受保護的私鑰儲存位置，輸入兩次遮罩的私鑰保護密碼，產生 ECDSA P-256 金鑰。私鑰只匯出為加密 PKCS#8 PEM，使用 .NET PBE 的 AES-256-CBC、PBKDF2-HMAC-SHA256、600,000 iterations。私鑰保護密碼採相同 8～128 字元有效 Unicode 規則，但與登入密碼分開輸入及處理。公鑰匯出為 SubjectPublicKeyInfo PEM。實際儲存位置及金鑰操作須由使用者在公司環境完成，代理不代選外部路徑或保存正式私鑰。

公司把公鑰交給建置流程，產生包含該公鑰的客戶端。公司端選擇加密私鑰檔並輸入保護密碼，再輸入內部帳號、顯示名稱及兩次登入密碼，簽發 `.pmauth`。兩種密碼在畫面中分開標示，不透過命令列、環境變數、腳本或檔案傳入。

發證工具支援開啟既有 `.pmauth`，先以目前私鑰對應的公鑰驗證，再維護整份帳號清單。新增／重設產生新 salt 與 hash，重設保留 userId。啟用／停用保留密碼驗證資料。儲存時增加 revision 並重新簽章，不在公司端維護客戶主機的 ProgramData。

修改後的 Key 需人工送到各離線主機重新匯入。重設與停用於下一次登入生效，已登入 session 沿用既有生命週期，不新增即時撤銷。舊 Key 的離線重放限制依第 7 節處理。

## 4. 授權檔格式與簽章

匯出的 `.pmauth` 與客戶端 `users.json` 使用同一種簽章 envelope。`users.json` 保存整份 envelope，不把驗證後的帳號拆成可直接修改的 unsigned JSON。

外層只允許下列欄位，未知欄位、重複欄位或型別錯誤一律拒絕。

| 欄位 | 規則 |
| --- | --- |
| formatVersion | 整數 1 |
| algorithm | 固定 `ECDSA-P256-SHA256-P1363` |
| keyId | SPKI DER 公鑰的 SHA-256，大寫 64 位十六進位，須命中內建信任公鑰 |
| payload | 下列 UTF-8 JSON 原始 bytes 的標準 Base64 |
| signature | IEEE P1363 固定 64 bytes 簽章的標準 Base64 |

payload 只允許下列欄位。

| 欄位 | 規則 |
| --- | --- |
| schemaVersion | 整數 2，與舊 unsigned schema 1 明確區分 |
| productId | 固定 `ProgramMigrationAnalyzer` |
| authorizationId | 非空 GUID，公司首次簽發時建立，同一份授權更新時維持 |
| revision | 1～2,147,483,647 的整數，同一授權更新增加 1，不得溢位 |
| issuedAtUtc | 明確 UTC 的 ISO 8601 日期時間，只供辨識，不依賴主機時鐘判斷是否有效 |
| users | 1～1,000 筆完整帳號記錄，允許全部停用 |

每筆 users 保留既有 `userId`、`username`、`displayName`、`isEnabled`、`passwordAlgorithm`、`iterations`、`salt`、`passwordHash` 欄位。帳號須已正規化，userId 與 username 不能重複，displayName 為 1～128 個 Unicode scalar values 且不能全空白。密碼演算法固定 PBKDF2-HMAC-SHA256，iterations 支援 600,000～2,000,000，salt 固定 16 bytes，hash 固定 32 bytes。新建及重設使用 600,000 iterations。

簽章輸入精確定義為下列 UTF-8 header bytes 後接解碼後的 payload 原始 bytes。`\n` 為單一 LF，最後一個 LF 也包含在輸入中。

```text
PMA-OFFLINE-AUTH/v1\n
ECDSA-P256-SHA256-P1363\n
<keyId>\n
```

使用 .NET 內建 ECDsa、SHA-256 及明確的 `IeeeP1363FixedFieldConcatenation` 簽章格式，不自行實作曲線運算，也不新增加密套件。公司端以固定欄位順序輸出無 BOM 的 UTF-8 payload。客戶端驗證原始 bytes，不重新序列化後驗證，避免 JSON 表示差異造成歧義。

讀取先限制 envelope 總大小 4 MiB、JSON 深度 16，限制解碼配置量，檢查外層格式與受信任 keyId，再驗證簽章，最後才解析 payload 與帳號規則。必須有界讀取，不能只檢查初始檔案長度後無限制讀取。無效 UTF-8、重複屬性、未知欄位、尾隨 JSON、非法 Base64、未知演算法或公鑰都拒絕。

公鑰只接受 ECDSA nistP256 的完整 SPKI，拒絕尾隨資料及私鑰 PEM。客戶端不得接受 Key 自帶的公鑰，也不能從旁邊的設定檔或環境變數覆寫信任根。

## 5. 首次啟用、更新與登入

正常啟動先讀取固定的 `%ProgramData%/ProgramMigrationAnalyzer/auth/users.json`，不依目前工作目錄改變路徑。

1. 有效簽章與安全 ACL：顯示原登入視窗，輸入帳密才可建立 session 與主工作區。
2. 檔案不存在、舊 unsigned schema 1、損壞、簽章不合法或 ACL 不安全：保持未登入，顯示授權匯入畫面及可讀原因分類。使用者可選公司提供的 `.pmauth` 或取消。
3. 匯入畫面只能選取授權檔，沒有建帳、修改帳號、設定密碼或啟用帳號欄位。顯示驗證通過的授權識別、revision 與帳號數，不顯示 salt、hash 或完整 payload。
4. 按「匯入授權」後重新讀取並驗證候選檔。一般程序需由使用者手動完成 Windows UAC，啟動同一 EXE 的獨立 `--import-authorization <檔案完整路徑>` 模式。命令列只傳路徑，不傳 Key 內容或密碼。
5. 提升權限程序重新讀取並驗證候選檔及目標狀態，顯示實際 authorizationId、revision 與帳號數供使用者確認，再固定寫入 ProgramData。它不使用父程序傳來的「已驗證」boolean，不建立認證 session 或主工作區。匯入成功後結束。
6. 原一般程序觀察子程序完成，重新讀取並驗證已安裝授權，核對 authorizationId／revision 與 payload 指紋和候選授權一致，成功後回到登入。取消 UAC 或失敗時保留匯入畫面與安全提示，沒有自動登入。

一般登入畫面提供「匯入／更新授權」，可維護既有部署。登入或匯入進行中停用重複操作，取消與晚到結果比照既有 coordinator 的 generation／生命週期檢查處理。

正式入口只接受無參數與唯一完整的 `--import-authorization <路徑>` 形式。舊 `--configure-local-account`、多餘參數、相對匯入路徑及任何跳過驗證的參數均拒絕。獨立匯入模式如果未提升權限，只提示授權匯入需要管理員權限並結束，不自動建帳。

每次登入重新讀取完整 envelope，檢查 ACL、公司簽章、帳號規則、啟用狀態及 PBKDF2。只有全部成功才能回傳登入成功。來源解析、分析、轉譯、六個操作入口與登出清理維持既有 session 契約。

## 6. 原子匯入、ACL 與舊資料

沿用 Administrators／SYSTEM FullControl、Users 目錄 ReadAndExecute／檔案 Read 的 ACL，防範一般使用者更動。Windows 提升權限只允許安裝已簽章授權，不能產生公司簽章。

匯入用有界讀取取得候選 bytes 並驗證。寫入採目標同目錄受保護暫存檔、獨占 lock、flush 與原子替換，替換後再次檢查 ACL 與簽章。失敗則回復原檔，保留可恢復的受保護 backup，不清空既有帳號設定。lock 等待及取消沿用現有有界機制。

寫入 API 只接受重新驗證過的簽章 envelope，不保留可任意改寫 users 的公開 callback。即使自行呼叫 Infrastructure，也無法把 unsigned 帳號寫成正式可登入資料。客戶端移除 `LocalAccountAdministrationService` 的 composition、管理視窗及建立／重設功能。

舊 unsigned schema 1 不自動轉成有效授權，也不自動沿用帳密。公司先簽發新 Key，客戶端以同一匯入流程替換。舊檔無效不阻擋匯入有效 Key，但替換成功前不得破壞舊檔。

本次設計與開發檢查不讀寫 SWANG-PC 的真實 `users.json`。未另取得新版本部署驗證授權前，保留目前可用的登入部署，不覆蓋真實帳號或 ACL。

## 7. 修訂、重放與公鑰生命週期

若目前存有有效授權，候選 authorizationId 相同時，revision 較低則拒絕。相同 revision 只有 payload 原始 bytes 完全一致時允許重複匯入，不同內容則拒絕。較高 revision 或不同 authorizationId 且簽章有效時允許整份替換。不同 authorizationId 必須顯示替換確認，避免選錯檔案。修訂檢查須在取得目標 lock 後重新讀取目前授權再執行，不能使用舊畫面快照決定可否覆寫。

revision 只阻擋正常流程誤匯入舊 Key。客戶管理員可刪除帳號檔或回復整個主機，因此沒有可靠防重放，舊 Key 不能靠新的停用 Key 在所有離線主機上失效。

公司私鑰遺失或外洩時，建立新金鑰、重建包含新公鑰的客戶端並重新簽發。公鑰更換需要公司建置，不允許客戶端匯入任意新公鑰。第一版只配置一把有效公司公鑰，不新增自動 key rotation 協定。

## 8. 元件邊界與發布

| 位置 | 責任與預計變更 |
| --- | --- |
| Core/Authentication | 保留 `IAuthenticationService`、LoginRequest、AuthenticationResult 與 IUserSession，不綁定 WPF 或金鑰儲存 |
| Infrastructure/Authentication | 授權 envelope／payload 模型、嚴格 parser、公司公鑰 verifier、讀取與原子匯入 store，PBKDF2 沿用現有實作 |
| App | 內建公鑰 composition、啟用／更新視窗、UAC 匯入程序、coordinator 啟動與返回登入，移除本機建帳模式 |
| 新增 LicenseIssuer 專案 | 公司用 WPF 發證工具、遮罩輸入、加密私鑰建立／載入、帳號清單維護與簽章，App 不引用此專案 |
| AuthenticationChecks | 真實 ECDSA、store、provider、正式 OnStartup 與匯入生命週期，測試公私鑰只在記憶體即時產生 |
| 文件 | 正式規格第 37 節、README、離線授權交付紀錄及實作進度，保留 Phase 4 歷史驗證證據 |

公鑰來源預設為建置端 `config/authorization-public-key.pem`，可由建置參數指定其他公鑰路徑，編譯成客戶端資源。這是公開資訊，可依公司決定納入版本控制。沒有有效公鑰的開發 build 保持未授權，不能登入，正式 publish 必須失敗並明確提示設定公鑰，不能內建測試公鑰或臨時信任根。

客戶端 `publish.bat` 維持最新作業與原目錄轉送流程，只發佈客戶端，更新「收件端自行建帳」提示為公司授權匯入。轉送時完整保留公鑰輸入設定，來源失敗或無有效公鑰時，不覆蓋原目錄既有 EXE。公鑰驗證後須固定本次建置所用的 bytes，不能在建置中途換檔而發佈其他公鑰。

公司發證工具使用獨立 `publish-issuer.bat` 及獨立輸出目錄，不複製進客戶端 `publish/win-x64-single-file`。任何 publish 都不得把私鑰、登入帳密、實際 `.pmauth` 或 ProgramData 帳號檔打包到 EXE 或輸出目錄。

## 9. 驗收要求

以下為新版本須執行的驗收，現在均未宣稱通過。

| 編號 | 情境與預期 |
| --- | --- |
| O1 | 無 users.json 首次啟動顯示匯入畫面，無主工作區，取消正常退出 |
| O2 | 公司簽發 Key 匯入後仍須帳密登入，正確帳密可分析／轉譯，未知／錯誤／停用帳號一致拒絕 |
| O3 | 修改 username、displayName、isEnabled、salt、hash、iterations、revision、keyId、signature 或 payload bytes 均無法放行 |
| O4 | 使用另一把未受信任私鑰簽發、未知演算法、錯誤產品、舊 schema 1 與非法結構均拒絕 |
| O5 | 有界讀取、深度、重複欄位、未知欄位、UTF-8、Base64、帳號數與 hash 參數邊界拒絕，不進行無界 PBKDF2 |
| O6 | 匯入失敗、取消、UAC 拒絕、一般權限、危險 ACL／reparse point、鎖定、寫入中斷與替換失敗不損壞原設定 |
| O7 | 每次重登重新驗章，安裝後修改帳號檔不能登入，舊管理參數無法建帳或開主畫面 |
| O8 | 發證新建／重設／停用產生完整有效 Key，8 字元與 @ ! # 密碼可用，無明文或共用預設帳密 |
| O9 | 私鑰加密匯出／載入的記憶體 roundtrip 通過，錯誤保護密碼拒絕，公私鑰匹配，敏感值不進入 ToString、log、Git 或客戶端交付 |
| O10 | 同一 Key 可在兩個隔離部署位置使用，相同／較低／較高 revision 與不同 authorizationId 遵守更新規則 |
| O11 | 無正式公鑰 publish 失敗，有效公鑰 publish 成功，原目錄轉送使用新版本，失敗不覆蓋舊 EXE，兩種交付目錄分離 |
| O12 | Solution build、AuthenticationChecks、Phase2Checks、RegressionChecks、WpfChecks、Scenario A／B／C、登出重登、輸出保留、HTML／純文字及無殘留程序回歸 |

測試只使用即時產生的記憶體測試金鑰與測試專用密碼。隔離帳號／授權檔與 log 位於當次 worktree 的 `.codex-tmp/YYYY-MM-DD_<task-name>/`，不提交。測試不得以 fake authentication 成功代替正式 provider／簽章驗證。ACL 與 UAC 替身的證據須和真實 Windows 部署觀察分開。

正式私鑰與實際帳號由使用者互動操作，代理不讀取或記錄值。新版本真實 ProgramData／ACL 及 UAC 部署驗證須先取得指定電腦與可替換資料的授權。未完成真實部署驗證時，不標記全部驗收完成或送 ready PR。

## 10. 開發與審閱流程

使用者已核准此規格與實作計畫，並指示依計畫作業。沿用同一實作者順序執行，不使用代理分工，不另建 worktree。

依 project AGENTS，實際開始當前 Phase 時才從最新遠端預設分支建立該 Phase 分支。審閱稿先保存於忽略的 `.codex-tmp/2026-10-01_offline-authorization-design/`，核准後在第一個實作 Task 移入正式 `docs/superpowers/specs/2026-10-01-offline-signed-authorization-design.md`，並連同計畫及 authoritative spec 更新形成基線。

每個實作 Task 完成後依專案流程驗證、commit／push、確認同步，再開始下一 Task。每個 Phase 全部驗證後建立唯一 PR，不自動 merge，不 force push，不預先建立後續 Phase 分支。原 detached checkout 的既有規格、治理文件、簡報及登入部署全部保留，只在發布任務中維護已授權的 publish 轉送腳本。

## 11. 官方技術依據

- [Microsoft：Cryptographic signatures](https://learn.microsoft.com/en-us/dotnet/standard/security/cryptographic-signatures)，公鑰驗證與私鑰簽章用來確認來源及完整性。
- [Microsoft：ECDsa.SignData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdsa.signdata?view=net-10.0)，內建 SHA-256 及指定簽章格式。
- [Microsoft：ExportEncryptedPkcs8PrivateKey](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.exportencryptedpkcs8privatekey?view=net-10.0)，加密私鑰匯出介面。
- [Microsoft：Windows security servicing criteria](https://www.microsoft.com/en-us/msrc/windows-security-servicing-criteria)，Windows 管理員掌握其主機的安全環境，不能把本機授權當成抵抗主機所有者的可靠邊界。

## 12. 草稿自審紀錄

2026-10-01：對照已確認需求、現行正式入口、store、PBKDF2、帳號管理與 publish 實作，檢查完整性、內部一致性、範圍與歧義。明確區分 Key 匯入與登入、公鑰與私鑰、簽章與密碼雜湊、正常修訂檢查與管理員重放限制。保留真實部署授權門檻，不把規格要求寫成已實作或已驗收。

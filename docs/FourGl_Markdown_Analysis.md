# 4GL 單檔離線 Markdown 分析

## 使用方式與範圍

在主視窗開啟一支 `.4gl`，按「開始分析」，即可在原有 Markdown 頁籤檢視並儲存報告。資料夾載入仍逐份文件分析，不合併其他程式內容。

這條分析流程不需要資料庫、網路、雲端或本機 LLM，也不執行來源中的 SQL、RUN、Shell、郵件或其他程式。既有 C# 分析、報告及 `.NET 8`、`.NET 10` 草稿介面維持原有用途，本次沒有擴充轉譯功能。

輸出沿用原有來源檔名加路徑雜湊的命名方式，以無 BOM 的 UTF-8 寫入 `output/`。同一來源路徑重新分析會更新該路徑的報告。WPF 的取消、文件切換、失敗狀態與純文字預覽備援維持原流程。

## 大型預覽與精簡報告

WebView2 改由本機 HTML 檔載入預覽，避免把整份大型 HTML 傳入 NavigateToString。每個視窗在輸出區的 `.preview/` 擁有獨立工作目錄，新頁完成導航後清理過期檔案，關閉後清理該視窗的 HTML，不刪除已儲存的 Markdown 或其他視窗的檔案。報告為靜態內容，停用頁面腳本與主程式物件存取。

初始化失敗與 HTML 寫入／導航失敗使用不同的提示及 Log，記錄例外種類與 HRESULT。載入失敗仍顯示純文字，後續文件可重新嘗試預覽，不因單次載入錯誤永久停用 WebView2。

正文以函式處理階段、條件後果、命令路由及欄位角色說明。一般賦值與作者註解不再逐句重印，互斥分支以成立時與否則區別。完整有效與停用 SQL 仍按每個來源位置列出一次，保留換行、字串空白及末行註解，不另重複整段 WHERE 的翻譯。

用途與規則解說由同檔證據推導，包括 COUNT 列數與數量加總的區分、COUNT(DISTINCT) 的去重範圍、更新欄位的指定值及 WHERE 邊界、未被後續有效讀取的回傳值、可辨識的 HHMMSS 數值門檻與兩個時間戳之差。游標投影與 CASE 對照可連結時，列出查詢選入但未明列對照、以及只保留在對照中的值。未能證明的正式商業定義仍保留待確認。

函式階段另保留影響判斷的短資料流，包括 INTERVAL 時間基準調整及相連別名、YYYYMMDD／HHMMSS 切片、欄位前綴到分支輸入，以及每筆命令重設與後續守衛呼叫。能證明分支沒有覆寫命令時，說明為何略過後續呼叫。前面已設定命令、出現未知副作用或其他可能覆寫時，不宣稱命令仍為初始值。

具體發現會交叉核對同表時間查詢與派送游標、靜態 UPDATE 寫入值與查詢比較值、只有郵件提醒的命令模板，以及明示 flag 欄位的作者註解與有效 RUN 條件。這些結論保留 SQL 編號或來源位置，只說明可辨識的同檔證據，不推定外部回寫或客戶正式商業名稱。游標或 UNLOAD 的 SELECT 即使與上下文同列，也獨立保留完整 SQL。

同一命令變數在多個路由之後的候選容量獨立限制為 256，避免一般值的 16 候選上限過早丟失最後命令。每個候選仍保留原有字串及條件長度限制，不能將有界候選視為已證明可執行的路徑。

## 報告內容

| 內容 | 可確認的證據 |
| --- | --- |
| 來源快照 | 讀取編碼、來源位元組數、位元組與解碼文字 SHA-256、行數及位置定義 |
| 程式用途 | 實際讀寫資料位置、計數接收變數及比較、輸入輸出、游標與明示呼叫角色 |
| 結構與流程 | MAIN、FUNCTION、REPORT、參數、回傳、IF、CASE、迴圈及原始順序 |
| 規則候選 | 外層路徑與分支條件、比較結果、賦值、狀態更新、RETURN、CONTINUE、STOP 的後果 |
| 命令路由 | 每個有效 RUN 的來源位置、運算式、拼接模板、未知參數及狀態接收 |
| 完整 SQL | 有效 DML 原文、尾行註解、限定表名、WHERE、INTO、游標及 UNLOAD 上下文 |
| 欄位與型別 | SQL 明示欄位、一般別名及子查詢作用域、更新欄位與 host value 映射、RECORD LIKE 的有效欄位引用 |
| 資料流 | LET、查詢接收、CALL RETURNING、同檔函式運算式、參數綁定、全域與區域變數讀寫 |
| 停用內容 | 完整註解 SQL 獨立分區，作者背景摘要另標註來源性質，不列為現行依賴或規則 |
| 完整度 | 辨識語句、未知語句、診斷、有界分析限制與未確認事項 |

報告把原碼事實、作者註解、語法推論與未知資訊分開。規則說明會將比較及後果寫成繁體中文，但不以函式名或客戶代碼硬編碼商業意義。例如，只有在 UPDATE 與其前置條件可證明時才說明某旗標的寫入，不把旗標值直接等同於外部命令失敗。

SQL 及宣告透過來源 span 擷取，不折疊字串內空白或換行。位置採 UTF-16 字元偏移，行與欄從 1 起算，結束偏移與結束欄不包含在範圍內。BOM 不屬於解碼文字快照，原始位元組雜湊則包含 BOM。

UTF-8 以嚴格解碼驗證，失敗時再嘗試 CP950 / Big5。兩者均無法無損解碼時明確拒絕載入，不以替代字元默默改變來源。直接由呼叫端建立的記憶體文件沒有來源位元組資訊，報告會明示未提供。

## 實作配置

| 檔案 | 責任 |
| --- | --- |
| `Core/FourGlEvidence.cs` | 快照、精確位置、語句樹、宣告、SQL、呼叫、命令、值與診斷證據 |
| `Parsers/FourGlLexer.cs` | 字串感知的詞彙分析，區分 `#`、`--`、`{...}` 註解並維持偏移 |
| `Parsers/FourGlSyntaxParser.cs` | 多行及區塊語句、CALL 參數與回傳、RUN 修飾詞、未知語法保留 |
| `Parsers/FourGlSqlReader.cs` | 4GL 專用 SQL 證據，保留完整原文並讀取表、欄位與上下文 |
| `Parsers/Informix4GlParser.cs` | 協調解析並投影回既有 Functions、ProgramUnits、SqlStatements、Dependencies 等共用模型 |
| `Analysis/FourGlEvidenceAnalyzer.cs` | 同檔有界符號分析、條件路徑、早退、變數讀寫與命令模板 |
| `Analysis/FourGlReportNarrator.cs` | 處理階段、查詢用途、條件後果、欄位角色及同檔交叉核對 |
| `Analysis/FourGlFlowInsights.cs` | 關鍵資料流、守衛呼叫、時間篩選範圍、狀態與提醒／註解交叉核對 |
| `Analysis/FourGlMarkdownFormatter.cs` | 繁體中文證據報告、安全 Markdown 與動態長度程式碼 fence |

`MarkdownReportGenerator.Generate` 的簽名維持原樣，只在 4GL 證據存在時使用新 formatter。C# 仍使用既有報告流程。共用 `SqlParsing.cs` 沒有改為新解析行為，因此不改變 C# SQL 投影。

所有來源檔名、註解及運算式在標題或表格內都經 Markdown 與 HTML escaping。原碼區塊的 fence 長度大於內容中的最長反引號序列，避免內容跳出證據區塊。實際驗證使用 WPF 同樣的 Markdig advanced extensions。

## 判讀限制

- 這是靜態結構與證據分析器，不是完整 Informix 4GL 編譯器。未支援語句或不完整區塊會列出診斷及原文，不宣稱已理解其副作用。
- 條件採符號候選，不進行完整邏輯可滿足性證明。不同分支的候選可能在實際環境不可同時成立，報告的候選不代表已驗證可執行路徑。
- 一般值最多保留 16 個候選，RUN 使用的命令變數最多 256 個候選，字串與候選條件最多 8192 字元。同檔呼叫最多展開 8 層，整次符號分析最多 200000 步，語法巢狀最多 128 層。超過限制時保留未知值或診斷。
- 迴圈以零次及一次符號展開合併，不推定實際迭代次數。多次賦值會保留最後命令的候選，而不把它當作全部已執行命令。
- 字串拼接及簡單算術有界展開。複合算術、時間轉換、資料庫結果、終端輸入及外部函式結果可保留為未知運算式，不猜測執行期值。
- `SELECT *`、`table.*` 與 RECORD LIKE 不展開外部 schema。COUNT(*) 與乘法不視為欄位 wildcard。資料庫欄位型別不由程式變數型別反推。
- 多表未限定欄位與名稱衝突保留待確認。支援常見別名與子查詢，不宣稱覆蓋所有 SQL 方言、複雜衍生表或查詢語意。
- PREPARE、EXECUTE 列出動態 SQL 模板與未知項目，不把動態模板列為已知靜態 DML 或資料庫字典。
- 註解 SQL 採保守區塊辨識，註解中的程式構想不代表目前仍會執行。註解與有效碼的語意矛盾仍需維護者確認。
- 不跨檔追蹤，不分析外部報表內部邏輯，不驗證郵件、FTP、排程、資料異動或外部命令成敗。一般程式用途說明不能取代人工確認的正式商業定義。

## 驗證紀錄

本次先在正式 RegressionChecks 加入合成來源，確認既有實作因多行 SQL 遺失 FROM 而失敗，再完成新實作。後續缺陷均補獨立合成回歸，包括 SQL CASE 與後續 4GL CASE 的邊界、INSERT VALUES 後的獨立 SELECT、早退回傳候選、分支命令及停用區塊內多個 SQL。

正式測試沒有納入客戶原始碼或客戶參考區路徑。

```powershell
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet build ProgramMigrationAnalyzer.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:minimal
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.Phase2Checks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.RegressionChecks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.WpfChecks
dotnet run --no-build --project tests/ProgramMigrationAnalyzer.PublishChecks
```

測試涵蓋精確 SQL span、尾行註解、字串中的註解標記、CP950 與不可解碼位元組、限定表名、逗號 join、子查詢別名、tuple UPDATE、host value、wildcard、RECORD LIKE 引用、CALL RETURNING、提早返回、CASE 多 RUN、最後命令、STOP、動態 SQL、遞迴上限、取消及實際 Markdown HTML rendering。

新增回歸以大量初始化與多路由的合成來源驗證報告篇幅、完整 SQL 僅列一次、互斥分支摘要、時間邊界及查詢／CASE 對照。WPF 另以 220 萬字元 HTML 驗證真實 WebView2 的本機檔導航、文件替換及預覽檔所有權與清理，不以縮小報告取代大型預覽測試。

關鍵資料流回歸使用另取名稱、旗標及初始值的合成程式，檢查時間調整與傳遞、前綴來源、每筆命令重設、未使用時間門檻的游標、只讀未寫的狀態、提醒路由與註解衝突。另驗證分支前已設定命令時不會誤稱略過守衛呼叫，並涵蓋括號條件、`==` 與同列游標 SELECT。

另外以使用者提供的本機資料做唯讀驗收，主驗收來源辨識 7 個程式單元、35 個有效 DML 與 11 個停用 SQL 區塊。有效 SQL 逐段與預期來源行段比對，35 段均保留完整原文。這些數字是測試結果，不是程式規則。其他本機資料可產生帶診斷的報告，不表示其所有方言或業務規則均已完整解析。

既有十支正式樣本、C# 舊報告及 4GL 的 `.NET 8`、`.NET 10` 草稿編譯回歸均持續檢查。WPF 檢查包含真正啟動、載入、分析、HTML 預覽、轉譯、儲存與關閉釋放。PublishChecks 使用隔離替身驗證既有發布腳本，並未執行新的正式發布。

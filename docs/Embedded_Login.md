# 內嵌雜湊離線登入

## 使用與維護

每次啟動輸入帳號與密碼，驗證成功才建立分析主視窗。不連網、不匯入 Key、不讀取外部帳號檔，不提供記住密碼、建帳、重設密碼或免登入參數。

帳號精確比對並區分大小寫。密碼保留大小寫與前後空白，最多 128 個 UTF-16 字元。帳號不存在與密碼錯誤使用相同提示，失敗後等待一秒才能重試。驗證期間禁止重複提交，仍可按取消或關閉視窗。取消後晚到的成功結果不會建立主視窗。

## 建置端設定

在正式專案根目錄執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Set-EmbeddedLogin.ps1
```

互動輸入帳號與兩次遮罩密碼。工具只保存帳號、salt、雜湊及參數，預設寫入 `config/embedded-login.json`，此檔已忽略追蹤。正式帳密及驗證資料不放入測試、文件、Git 或客戶交付清單。

重新設定既有資料需加入 `-Force`，不一致的確認密碼不會覆蓋既有設定。工具的 SecureString 參數供本機安全呼叫與合成測試使用，不接受明文密碼參數。實際密碼不得放入命令列、環境變數、腳本或文字檔。

建置以固定資源名稱 `ProgramMigrationAnalyzer.EmbeddedLogin.json` 嵌入 App。缺少設定則建置失敗，設定內容損壞則執行時拒絕啟動。`PmaCredentialFile` 僅為建置時選擇驗證資料的屬性，不是客戶端執行時覆寫機制。

更換帳密後必須重新建置及部署。舊版 EXE 仍使用原先內嵌資料，不會因本機設定改變而自動撤銷。

## 密碼處理

採用 PBKDF2-HMAC-SHA256、600,000 次迭代、16 bytes 隨機 salt 與 32 bytes 雜湊。每次產生設定使用新的 salt，執行時用固定時間比較雜湊。設定端與驗證端均採嚴格 UTF-8，拒絕不合法的 UTF-16 輸入，不以替換字元改變密碼。派生後的密碼位元組與候選雜湊會清除，PasswordBox 在提交及關閉時清空。

輸入密碼在執行驗證時短暫存在程序記憶體，不能承諾 managed 字串立即從記憶體抹除。程式不保存明文、不將密碼寫入 Log，也沒有將解密金鑰與密碼密文放在程式內的機制。

演算法與迭代數參考 [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)，固定時間比較使用 [.NET CryptographicOperations.FixedTimeEquals](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals?view=net-10.0)。

## 驗證邊界

測試使用合成帳密。PBKDF2 預期值由 Python hashlib 獨立產生，測試真正的 .NET 驗證器及 Windows PowerShell 設定工具，沒有用永遠成功的假驗證器取代正確登入。

WPF 整合涵蓋只有登入視窗、錯誤與未知帳號、空白、取消、重複提交、例外不洩漏、晚到成功、登入成功建立單一工作區及原有分析流程。獨立程序涵蓋真實內嵌資源的取消退出、損壞資料拒絕啟動及拒絕 `--skip-login`。

PublishChecks 使用隔離替身驗證發布腳本，不代表實際發布或客戶環境驗收。本次授權只有本機實作與驗證，現有 `publish` EXE 未更新。

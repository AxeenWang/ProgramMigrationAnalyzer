# 混淆相容性評估

日期：2026-10-26。此文件是靜態相容性評估，不代表已安裝、套用或驗收混淆工具。

## 專案觀察

| 範圍 | 現況與必須保留的關係 |
| --- | --- |
| WPF | App、MainWindow、LoginWindow 的 XAML 會編譯為 BAML，型別、事件處理及資源 URI 必須保持一致 |
| MVVM | CommunityToolkit.Mvvm 產生命令及屬性，XAML Binding 使用成員名稱，不能任意重新命名綁定的公開成員 |
| 控制項 | Behaviors、Converters、AvalonEdit 與 WebView2 有 XAML 及事件整合，第三方元件第一輪不混淆 |
| 驗證資料 | 新驗證器以 JsonDocument 讀取固定欄位，沒有依 DTO 成員名稱做反射序列化。內嵌資源名稱是明確的執行契約，不能任意更名 |
| 核心分析 | Analysis、Parsers、Translation 以直接型別及方法呼叫為主，但跨 assembly 的公開契約必須同步處理或保留 |
| 單檔發布 | 現行設定為 win-x64、自包含、PublishSingleFile、IncludeNativeLibrariesForSelfExtract，PublishTrimmed=false |

原始碼搜尋未發現應用程式以 Assembly.Load、Activator、GetMethod 或 GetProperty 動態尋找業務成員。這不等於框架與第三方元件沒有反射，WPF 綁定本身仍是主要相容性邊界。

## 建議的後續做法

先選擇明確支援 .NET 10 與 WPF BAML 的工具，確認商業使用授權，再做隔離試驗。不因工具標示支援 WPF 就假設目前版本及所有綁定均可直接處理。

第一輪保留所有 XAML 引用、Binding 路徑、資源 URI、跨 assembly 公開契約及第三方程式庫，只評估自有核心實作與驗證邏輯的保護。避免先啟用成員刪除、激進合併、裁剪或 NativeAOT，因為這些不是混淆必要條件。

混淆應處理打包前的 managed assemblies，再由 SDK 打包單檔，不能假設對最終 EXE 改名或加殼即可正確處理。後續整合須依 SDK 的 PrepareForBundle / GenerateSingleFileBundle 擴充點與工具文件確認次序。

後續試驗必須以真正混淆後的成品重做登入、取消、分析、轉譯、批次、大型 WebView2、檔案列表及關閉釋放。另需檢查 WPF Binding 錯誤、內嵌驗證資料可讀性、符號對照檔保管及乾淨客戶環境。現有未混淆測試通過不能取代這些驗收。

混淆增加理解與修改程式的成本，不能保證對掌握主機權限的人隱藏所有程式或防止繞過登入。本次不安裝工具、不修改混淆管線，也不產生正式發布成品。

## 官方資料

- [PreEmptive：Transform XAML](https://support.preemptive.com/hc/en-us/articles/43293156248977-Configure-Transform-XAML)，說明 XAML/BAML 引用同步與建置後測試。
- [Redgate：Troubleshooting after building](https://documentation.red-gate.com/sa/troubleshooting/common-issues/troubleshooting-after-building)，說明以名稱反射及序列化遇到重新命名的風險。
- [Microsoft：Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)，說明單檔部署限制與打包前擴充點。

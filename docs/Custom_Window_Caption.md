# 自訂標題列

主視窗與登入視窗共用深藍標題列，文字綁定 Window.Title，版號取自 App 組件，目前為 1.0.1.0。標題也保留在 Windows 工作列與 Alt+Tab，不依賴目前啟動程式或測試組件的版本。

主視窗提供最小化、最大化／還原及關閉。登入固定大小，只提供關閉。關閉仍走原有視窗生命週期，登入驗證中的晚到結果不會建立主視窗，分析工作區的關閉清理與成果保留規則維持原樣。

空白標題區可拖曳，雙擊可最大化或還原，右鍵顯示系統選單，左上圖示也保留原生系統選單命中區域。邊框保留縮放操作。Windows 11 最大化按鈕回傳 HTMAXBUTTON，供系統辨識貼齊配置。

視窗最大化時，客戶區依當次目標螢幕的工作區計算，避免自訂框架覆蓋工作列或將標題列裁出螢幕。命中座標保留正負號，使用 WPF 螢幕座標轉換處理目前 DPI。自訂按鈕含可辨識的操作名稱、鍵盤焦點及最大化／還原提示。

貼齊的可用配置仍受既有最小尺寸 1100×720 與螢幕空間限制。不同 DPI、多螢幕組合與所有 Windows 個人化設定尚未全面驗證，不將原生命中測試等同於每種桌面環境的完整驗收。

## 實作位置

- `Assets/ApplicationIcon.xaml` 是程式括號與向右遷移箭頭的原創向量圖示，`ProgramMigrationAnalyzer.ico` 包含 16、20、24、32、40、48、64、128、256 像素，PNG 為預覽。
- App 的 ApplicationIcon 將 ICO 嵌入 EXE，CustomWindowStyle 設定 Windows 視窗圖示，自訂標題列使用同一設計的向量圖像。這些資產均編譯進程式，不增加客戶端交付檔案。
- `Controls/WindowTitleBar.xaml` 與 code-behind 共用外觀、標題綁定與視窗操作。
- `Resources/ApplicationResources.xaml` 的 CustomWindowStyle 將標題列包在既有內容外，不改動分析版面。
- `Behaviors/CustomWindowFrame.cs` 設定 WindowChrome，處理最大化、系統圖示及標題的命中、滑鼠捕捉與工作區邊界。
- 兩個視窗的 constructor 套用共同行為，保留自己的 Title 與 ResizeMode。

檔案列表的「全選 .4gl」與「全部取消」使用帶勾選圖形的圓角按鈕，保留原有文字、ICommand 及勾選行為。數量徽章位於區塊標題右側，讓操作在窄側欄保留並排空間。樣式包含懸停、按下、鍵盤焦點及停用狀態。

## 驗證邊界

WpfChecks 使用真正顯示的 WPF 視窗及原生視窗訊息，驗證 HTCAPTION、HTSYSMENU、HTMAXBUTTON、縮放邊框、負螢幕座標、最小化、最大化與還原、雙擊、捕捉取消及最大化工作區。登入測試透過自訂關閉鈕驗證程序退出及晚到成功不生效，原有分析、轉譯、預覽及排版回歸持續執行。

圖示驗證由 Windows 實際取出建置 EXE 的圖示，再與專屬 ICO 的對應尺寸比較，另檢查兩個視窗與標題列確實有載入圖示。不能只以來源檔存在或專案設定推斷 EXE 圖示已正確更新。

擷取的 WPF 圖片用於檢查共用標題列、按鈕及內容版面。這些圖片不涵蓋作業系統繪製的選單，也不能替代各客戶桌面環境的操作驗收。

## 官方依據

- [Microsoft：WindowChrome](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome?view=windowsdesktop-10.0)
- [Microsoft：Windows 11 Snap Layouts](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu)
- [Microsoft：HwndSource](https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.hwndsource?view=windowsdesktop-10.0)

# 第一階段架構

同一個 EXE 有兩種正式模式：無參數顯示設定視窗；`--monitor` 在 SYSTEM 工作排程中持續監控。`--probe` 只做一次真實外網檢查，以退出碼 0 表示通過、2 表示未通過。首次互動啟動（尚無設定檔與監控工作）會在顯示視窗前執行外網預檢；三個檢查服務都沒有回應時提示並結束。任何一個服務回應即通過，已設定的電腦仍可開啟視窗管理或停止監控。

## 設定介面

`MainForm.cs` 負責選擇網卡、Wi-Fi、提示密碼及啟用／停止。網卡以 GUID 記錄，修復時不依賴會改名或重複的顯示名稱。選擇 Wi-Fi 之外的網卡時，Wi-Fi 區塊停用。掃描與安裝在背景執行，介面不會在處理期間被關閉；較舊的掃描結果不會覆寫較新的選擇。

介面以 96 DPI 為設計基準，建立控制項時暫停版面配置後統一縮放；內容畫布保留縮放後最小尺寸，小螢幕透過捲軸操作。底部以 TableLayoutPanel 分隔狀態列與操作列，避免標籤的字體縮放擴大邊界而遮住按鈕。

## Windows 網路整合

`Networking.cs` 以 `Get-NetAdapter -IncludeHidden` 列出網卡，透過 WLAN API 取得 Wi-Fi 清單、讀寫 Windows 連線設定及送出連線要求。新設定使用 `connectionMode=auto` 並建立為所有使用者可用的設定。只在記憶體中組合包含密碼的 XML，由 `WlanSetProfile` 交給 Windows 處理。

重啟網卡的操作只插入經過解析的 GUID，不插入使用者提供的網卡名稱、SSID 或密碼。網卡重新啟用放在 `finally`；另處理 Wi-Fi 軟體無線電仍關閉的情況。

## 監控與安裝

`RecoveryEngine.cs` 對三個外部服務平行檢查有效回應，任一成功就視為可以上網。持續時間使用單調計時，避免時區或校時影響。開機等待期間仍記錄持續斷網，但不操作網卡或重新啟動。

應用透過工作排程以 SYSTEM 執行，開機觸發並定期補啟動；多重執行受排程與命名 Mutex 保護。不需要使用者保持登入或開啟設定視窗。

所有可由 SYSTEM 執行的應用檔案都安裝在受保護的 Program Files 目錄，設定置於受保護的 ProgramData 目錄。標準使用者只可讀取，無法替換背景執行的程式或設定。

排程讀取僅在 `GetTask` 邊界將 `0x80070002`／`0x8004130F` 視為不存在，涵蓋 COMException 與 Framework 映射出的 FileNotFoundException；服務連線、存取權限及其他 HRESULT 不視為未安裝。此讀取路徑同時用於狀態、安裝快照與停止／還原流程。

## Windows 官方參考

- [WlanGetAvailableNetworkList](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlangetavailablenetworklist)
- [WlanSetProfile：所有使用者設定及權限](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetprofile)
- [WlanSetInterface：軟體無線電](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetinterface)
- [WPA3-Personal transition profile](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wpa3-personal-transition-profile-sample)

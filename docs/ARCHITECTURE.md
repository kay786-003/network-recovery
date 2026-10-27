# 第一階段架構

同一個 EXE 的模式：無參數顯示設定視窗；`--monitor` 在 SYSTEM 工作排程中持續監控；`--tray` 在安裝者的互動登入 session 顯示系統匣。`--probe` 只做一次真實外網檢查，以退出碼 0 表示通過、2 表示未通過。首次互動啟動（尚無設定檔與監控工作）會在顯示視窗前執行外網預檢；三個檢查服務都沒有回應時提示並結束。任何一個服務回應即通過，已設定的電腦仍可開啟視窗管理或停止監控。

## 設定介面

`MainForm.cs` 負責選擇網卡、Wi-Fi、提示密碼及啟用／停止。網卡以 GUID 記錄，修復時不依賴會改名或重複的顯示名稱。選擇 Wi-Fi 之外的網卡時，Wi-Fi 區塊停用。掃描與安裝在背景執行，介面不會在處理期間被關閉；較舊的掃描結果不會覆寫較新的選擇。

介面以 96 DPI 為設計基準，建立控制項時暫停版面配置後統一縮放；內容畫布保留縮放後最小尺寸，小螢幕透過捲軸操作。底部以 TableLayoutPanel 分隔狀態列與操作列，避免標籤的字體縮放擴大邊界而遮住按鈕。

## Windows 網路整合

`Networking.cs` 以 `Get-NetAdapter -IncludeHidden` 列出網卡，透過 WLAN API 取得 Wi-Fi 清單、讀寫 Windows 連線設定及送出連線要求。新設定使用 `connectionMode=auto` 並建立為所有使用者可用的設定。只在記憶體中組合包含密碼的 XML，由 `WlanSetProfile` 交給 Windows 處理。

重啟網卡的操作只插入經過解析的 GUID，不插入使用者提供的網卡名稱、SSID 或密碼。網卡重新啟用放在 `finally`；另處理 Wi-Fi 軟體無線電仍關閉的情況。

## 監控與安裝

`RecoveryEngine.cs` 對三個外部服務平行檢查有效回應，任一成功就視為可以上網。持續時間使用單調計時，避免時區或校時影響。開機等待期間仍記錄持續斷網，但不操作網卡或重新啟動。

D0007 的 `RestartBudget.cs` 保存最多五筆重啟預約，逐筆以 4 小時滾動窗到期；外網檢查成功才全部清零。預約必須先原子寫入 `restart-history.xml` 並 Flush 至磁碟，才呼叫 shutdown；命令失敗或程序被中斷也保留該筆，避免崩潰重送繞過上限。滿額或寫入失敗時回到 180 秒修復／60 秒觀察循環，不停掉網卡修復。狀態損壞時保守視為剛用完五次，外網恢復或等待 4 小時可解除；讀寫失敗不允許重啟。安裝、停止及重新啟用不刪除計數。

同一次開機（以 Windows LastBootUpTime 識別）內以 GetTickCount64 計算歷時，程序重啟不改用牆鐘。跨 Windows 開機無共用單調時鐘，因此只在載入不同開機的狀態時用 UTC 差值銜接，之後換回單調時間；UTC 倒退視為零歷時，跨開機 UTC 大幅前進可能使額度提前到期。此限制與錯誤處理見 D0014，不使用本地時間計算，也不支援跨睡眠。檔案沿用受保護 ACL、拒絕重新解析點及硬連結；不含 SSID、GUID 或密碼。

應用透過工作排程以 SYSTEM 執行，開機觸發並定期補啟動；多重執行受排程與命名 Mutex 保護。不需要使用者保持登入或開啟設定視窗。

所有可由 SYSTEM 執行的應用檔案都安裝在受保護的 Program Files 目錄，設定置於受保護的 ProgramData 目錄。標準使用者只可讀取，無法替換背景執行的程式或設定。

排程讀取僅在 `GetTask` 邊界將 `0x80070002`／`0x8004130F` 視為不存在，涵蓋 COMException 與 Framework 映射出的 FileNotFoundException；服務連線、存取權限及其他 HRESULT 不視為未安裝。此讀取路徑同時用於狀態、安裝快照與停止／還原流程。

D0008 使用掃描的可見 SSID（排除只有已儲存、沒有 BSSID 的項目），以主要 SSID 的 hex 識別；舊設定從 Windows profile 唯讀取得 SSID，不匯出明文金鑰。主要目標不在、其他 SSID 可見時跳過網卡修復／重啟並每約 30 秒掃描與嘗試已儲存候選；一個 SSID 都不可見或掃描失敗時回到修復流程。候選主要設定優先，其餘按名稱排序輪流連線，不要求或保存其他密碼；連線要求失敗不能撤銷已確認的目標不在判定。

D0011 使用同一個 `RestartRequest` 閘門：預約額度、持久化額度、寫入 UTC 時間與原因並 Flush 成功，最後才要求 shutdown。紀錄失敗不重啟；不增加診斷包或事件匯出。

`MonitorControl.cs` 以受保護 `control.xml` 保存暫停起點與時長，`status.xml` 保存最近的外網結果及執行狀態。系統匣右鍵只列四個暫停時長，以持久化狀態同步勾選；點已勾選項取消，點另一時長重新起算，過期不勾選。讀取失敗停用選項，寫入成功後才更新勾選，不以 UI 本身狀態判定是否暫停。暫停時仍探測但不自動操作網路；到期／取消時清除重啟額度並重新起算 180／60 秒，已處理的 revision 隨額度保存，避免程序重開重複清零。控制檔無法讀取時暫緩自動動作。跨開機的暫停只以 UTC 銜接一次，隨即保存為本次開機的單調時間基準。Global action mutex 將設定切換、暫停、手動修復及自動網路操作序列化；操作中暫停／升級會提示稍後重試，不強制中斷網卡操作。網卡修改子程序即使超過原等待時間仍保留操作鎖直到結束，避免 finally 重新啟用前又啟動另一個操作。

`NetworkRecovery Tray` 使用安裝者 SID、InteractiveToken 及最高可用權限，在該帳戶登入後啟動；不以 SYSTEM 顯示 UI。停止監控保留系統匣，關閉系統匣保留監控。安裝先保存原有 monitor、tray、legacy 排程快照，停掉舊 monitor／tray 才替換檔案；新排程先以停用狀態註冊，最後停用 legacy、啟動新 monitor 與 tray 並確認持續執行。錯誤會還原原檔及排程，還原不完整則保留備份並報錯。從已安裝 EXE 單純重存設定時保留現有 tray 排程，不停止啟動設定視窗的父排程；外部新版 EXE 升級才替換既有 tray。只處理這三個已知排程，無法清除任意名稱或手動啟動的舊腳本。

`ManualRepair.cs` 只由互動視窗呼叫，沒有排程或命令列入口。「監控工具」只有備份、還原備份、一鍵修復並重設、移除備份四個按鈕。備份保存目前使用者三個 Proxy 值（含不存在狀態）、WinHTTP／IPv4／IPv6 netsh dump、Winsock 登錄設定與校驗，不覆寫既有備份。一鍵修復需先確認，已有備份須通過帳戶與完整性驗證；沒有備份則先建立，再清 DNS、修復所選網卡、重設 Proxy／Winsock／IP。備份僅管理員／SYSTEM 可讀寫。WTS session SID 必須符合目前 token，防止以 SYSTEM 或其他管理員帳戶修改錯誤 HKCU。重設失敗會嘗試各備份還原並保留資料；還原成功亦可能需要重啟。VPN 廠商元件及驅動差異仍需實機驗收，不能保證登錄／IP 還原涵蓋第三方安裝程式的全部效果。手動修復只重設斷網觀察計時，不清除重啟額度；修復及還原成功後只顯示單一「確定」的重啟提醒，不呼叫 `RestartRequest` 或 shutdown；按確定只關閉提醒。自動監控的重啟閘門維持原流程。

## Windows 官方參考

- [netsh Winsock](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-winsock)
- [netsh interface 備份與還原](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-interface)
- [netsh WinHTTP](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-winhttp)
- [WlanGetAvailableNetworkList](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlangetavailablenetworklist)
- [WlanSetProfile：所有使用者設定及權限](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetprofile)
- [WlanSetInterface：軟體無線電](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetinterface)
- [WPA3-Personal transition profile](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wpa3-personal-transition-profile-sample)

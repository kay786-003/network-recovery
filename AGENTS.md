# Agent 工作規範

## 適用範圍與閱讀順序

本檔適用於整個儲存庫。每個新 session 先閱讀本檔、`README.md`、`docs/ROADMAP.md`、`docs/ARCHITECTURE.md`、`CHANGELOG.md`、`docs/VERIFICATION.md`，再執行使用者當次要求。待辦事項不代表已授權開發或發布；不要自行開始第二階段。

使用 Windows PowerShell 與 Windows Git，遵守父層工作區規範；不要在 WSL 或舊副本開發。所有指令以目前 clone 的專案根目錄為基準，不把個人電腦的絕對路徑寫入程式或共享文件。

## 固定目錄與文件

- 應用原始碼只放 `src/`；安全、無網路副作用的測試只放 `tests/`。
- `build.ps1` 是編譯入口；`package.ps1` 是封裝入口；產物只放 `dist/`。
- 正式文件的路徑、大小寫、標題與章節順序由 `docs/documentation-schema.json` 登記，使用 `tests/documentation.ps1` 檢查。
- 根目錄固定文件：`AGENTS.md`、`README.md`、`CHANGELOG.md`、`CONTRIBUTING.md`、`SECURITY.md`。
- `docs/` 固定文件：`ARCHITECTURE.md`、`VERIFICATION.md`、`ROADMAP.md`、`DECISIONS.md`。
- 不建立大小寫重複檔案、另一份 README、零散的 session 摘要或臨時設計文件。臨時材料放被忽略的 `work/`。
- 確需新增或改名正式文件時，同步更新文件登記、README 索引、封裝規則及相關連結，並記錄原因；不要只移動檔案。

## 技術與行為契約

- 維持 C# 5、.NET Framework 4.8、Windows Forms、單一 EXE 與 `requireAdministrator` manifest。未經使用者要求不改框架、不新增執行期相依或 NuGet 套件。
- 第一階段只處理一張使用者選擇的網卡及一個 Wi-Fi，以 GUID 識別網卡，禁止硬編碼本機 SSID、網卡 GUID 或使用者資料。
- 正式預設：持續斷網 180 秒修復網卡；修復後 60 秒仍無法上網才重啟。開機等待 300 秒，等待期間累計斷網；持續無網到期即進入修復，不再額外等待 180 秒。
- 重啟次數上限（D0007）：連續 4 小時內最多重新啟動 5 次；任一次外網探測成功即歸零並重新起算 4 小時。達到上限後不再重新啟動，只繼續修復網卡與重新連線。這是使用者指定的正式預設，未經使用者要求不得改動或再加其他上限。
- 計時使用單調時間，避免 UTC／本地時間混算。使用前提為關閉睡眠與休眠，不實作也不驗收跨睡眠／休眠的計時（D0010）。
- 必須檢查真實外網回應，不能只憑 Wi-Fi 連線圖示判斷上網成功。維持單一可用性判定，不做 DNS／TCP／HTTP 分層探測或多端點診斷（D0009）。
- 目標 SSID 不在範圍時依 D0008 處理：看得到其他 SSID 但看不到目標視為目標不在，不消耗重啟次數、改為持續掃描；一個 SSID 都看不到才視為網卡異常。其他已儲存 SSID 的自動重試只能使用 Windows 已儲存的憑證，不得新增密碼欄位。
- 重新啟動前必須先寫入一筆含時間與原因的簡易紀錄（D0011）；不建立診斷包或事件時間軸匯出。
- 開啟設定視窗不啟用監控；只有使用者按「儲存並啟用」才安裝／切換背景工作。安裝失敗要保留或還原原有監控。
- 保留 `NetworkRecovery Monitor` 排程名稱及 Program Files／ProgramData 的 `NetworkRecovery` 路徑，變更時必須提供遷移方式並更新架構決策。

## 密碼、權限與實機操作

- Wi-Fi 密碼只能在記憶體及 Windows WLAN API／Windows 連線設定中處理，禁止寫入應用設定、日誌、命令列、測試報告或儲存庫。
- 不輸出 token、私鑰、真實密碼、個人 SSID／GUID 或完整私人診斷；不要讀取其他專案的憑證。
- 保留受保護的安裝／設定 ACL、重新解析點防護及安裝還原流程，不為了通過測試移除權限檢查。
- 預設測試不得安裝正式監控、寫入真實 Wi-Fi 設定、切換連線、停用網卡或重啟電腦。這些操作須有使用者對當次實機測試的明確授權。
- `legacy/`、`work/`、`dist/`、本機設定、日誌與秘密檔案不得提交。歷史腳本中的重啟實測不等於新版 EXE 已通過驗收。

## 變更與驗證流程

1. 先查看 `git status --short` 和相關程式／文件，保留使用者未提交的變更。
2. 只完成當次授權範圍；不要無故重排、改名、重寫整個專案或修改其他專案。
3. 執行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\documentation.ps1`。
4. 執行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`。
5. 執行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1`。
6. 影響封裝時再執行 `package.ps1`，確認公開原始碼包不含本機資料。若改動 UI，以示範資料檢查畫面。
7. 功能／行為變更更新 `CHANGELOG.md` 的 `[Unreleased]`；架構變更追加 `docs/DECISIONS.md`；驗證結果更新 `docs/VERIFICATION.md`，明列沒有執行的測試。
8. 檢查 `git diff --check`、變更清單與隱私資料，再交付。既有測試通過後不要無理由重跑或進行實機破壞性測試。

## 版本、提交與交付

`src/AssemblyInfo.cs` 是 EXE 版本來源。沒有正式發行要求時，不自行建立版本標籤或把 `[Unreleased]` 宣稱為已發布版本。

每次建置、打包或交付供使用者執行的 EXE，檔名必須包含版本號，格式為 `NetworkRecovery-v<產品版本>.exe`（例如 `NetworkRecovery-v0.1.0.exe`）；Windows ZIP 內的 EXE 也適用，不能只在 ZIP 名稱或外層資料夾標示版本。版本必須取自 `src/AssemblyInfo.cs` 的 `AssemblyInformationalVersion`，並核對成品的產品版本，禁止只改檔名冒充新版本。同一產品版本的未發行測試建置，檔名再加建置時間（例如 `NetworkRecovery-v0.1.0-dev-20261027-143000.exe`），避免不同修正版混淆；此後綴只識別建置，不代表正式發行或版本升級。

修改建置／封裝流程時，必須同步檢查 `build.ps1`、`package.ps1`、`release.ps1`、測試、校驗清單及文件引用，確保交付的 EXE 與 ZIP 內容遵守上述命名。安裝到 Program Files 後供既有排程執行的內部檔名仍保留 `NetworkRecovery.exe`，不得因交付檔名改變而破壞排程路徑。交付時明列實際 EXE 檔名與位置。

發行固定使用 `release.ps1 -Version <major.minor.patch>`，產物放 `dist/releases/v<版本>/`；EXE 產品版本、四段式檔案／manifest 版本、Changelog 與 `v<版本>` 標籤必須一致。`.github/workflows/release.yml` 在版本標籤推送後重新建置並發布預發行附件。尚未完成實機驗收時，不移除預發行標示。公開狀態是使用者指定的設定，不擅自改回私人。

提交訊息採 `type: 簡短說明`，type 使用 `feat`、`fix`、`docs`、`test`、`build`、`chore`。不可 force push、改寫歷史或刪除遠端分支來簡化工作。建立／推送儲存庫、發布 Release 及改公開狀態依使用者授權辦理。

交付時說明改了什麼、測了什麼、哪些實機項目尚未驗證；不要聲稱沒有執行過的檢查已通過。新 session 不依賴聊天紀錄猜測專案狀態，應以這些固定文件和實際程式為準。

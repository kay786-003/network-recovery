# Changelog

日期使用 `YYYY-MM-DD`。開發中的變更只記錄在 `[Unreleased]`，只有真正發行才新增 `## [版本] - 日期`。每個版本固定使用下列四個子章節，沒有項目時填「無。」。

## [Unreleased]

### Added

- 首次設定前檢查外網探測服務；全部無回應時提示使用者並結束程式。
- 新增待修復問題清單，記錄外網探測、簽章及實機驗收待辦。

### Changed

- README 明列 Windows 10/11、.NET Framework 4.8 Runtime、PowerShell/NetAdapter 前置條件，並連結 Microsoft 官方下載頁。

### Fixed

- 修正底部狀態標籤在高 DPI 下額外放大並遮住啟用／停止按鈕；以 TableLayoutPanel 分隔狀態、提示與操作區，新增示範資料版面回歸檢查。
- 修正高 DPI 下的文字裁切與控制項走位：採用 96 DPI 設計基準，統一縮放網卡欄位與圖示，小視窗提供捲動。
- 修正更新密碼勾選框被密碼標題遮住，以及預覽圖片底部裁切。
- 修正不存在排程的 0x80070002 被 Framework 映射為 FileNotFoundException 時，狀態讀取及首次安裝失敗；僅將 GetTask 的指定不存在 HRESULT 視為未安裝，其他錯誤仍保留。
- 已有設定的互動啟動不再同步查詢背景排程，避免不必要的啟動延遲。

### Security

無。

## [0.1.0] - 2026-10-24

### Added

- 第一階段 Windows EXE：列出網卡、選擇一張網卡與一個 Wi-Fi，設定自動重連。
- Windows Wi-Fi 密碼設定、更新已存密碼，以及連線確認。
- 背景外網檢查、指定網卡修復、開機恢復與必要時重新啟動電腦。
- 單一 EXE 的管理員權限宣告、背景排程、權限保護及安裝失敗還原。
- 固定的 Agent 工作規範、貢獻說明、安全說明、路線圖、架構決策及文件格式檢查。
- Windows GitHub Actions 建置與無副作用測試。
- 公開儲存庫與 `v0.1.0` 預發行版本，提供 EXE、Windows 套件、原始碼套件及 SHA-256 校驗檔。
- 固定 `release.ps1` 發行準備入口及標籤觸發的 GitHub Release 流程。

### Changed

- 共享專案使用固定文件登記及明確的封裝檔案清單，排除舊腳本與本機紀錄。

### Fixed

無。

### Security

- Wi-Fi 密碼不進入應用設定、日誌、命令列或 Git 儲存庫。
- 原始碼與 Windows 封裝排除私人設定、日誌及歷史診斷資料。

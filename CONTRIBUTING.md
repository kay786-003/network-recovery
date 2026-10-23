# 貢獻指南

## 開始之前

先閱讀根目錄 `AGENTS.md`，再查看 README、路線圖、架構及驗證紀錄。文件大小寫和位置固定；不要在自己的 session 建立另一套規則。待辦功能不代表當次工作範圍。

## 開發環境

使用 Windows PowerShell、Windows Git 與 .NET Framework 4.8。此專案以 Framework C# 編譯器建置，不需要 .NET SDK 或 NuGet 套件；語法維持 C# 5。Visual Studio 可開啟根目錄 `NetworkRecovery.csproj`。

## 變更流程

先確認工作目錄沒有需要保留的變更，再完成聚焦的修改。程式放 `src/`，測試放 `tests/`，正式文件依 `docs/documentation-schema.json`；草稿與個人報告放被忽略的 `work/`。

影響使用者的變更記入 `CHANGELOG.md` 的 `[Unreleased]`；重要技術決定追加 `docs/DECISIONS.md`，不覆寫歷史決定。不要把個人 SSID、網卡識別碼或憑證放進測試。

## 必要檢查

在專案根目錄執行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\documentation.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1
git diff --check
```

封裝變更再執行 `package.ps1` 並檢查壓縮檔。預設檢查不操作真實網卡、密碼、排程或電腦重啟；實機驗收另依 `docs/VERIFICATION.md`，並先取得當次測試授權。

## 提交與合併

使用 `type: 簡短說明` 提交訊息。PR 描述說明問題、改後行為、測試及未驗證的限制。不要為了方便 force push 或把產物、私人紀錄加入 Git。

## 文件格式

Markdown 使用 UTF-8、單一 H1、固定 H2 順序，列表前留空行。固定章節以登記表為準；內容可更新，不任意改名或重排章節。新增正式文件時同步更新登記、README 索引及封裝規則。執行 `tests/documentation.ps1` 可驗證大小寫、標題、章節與本地連結。

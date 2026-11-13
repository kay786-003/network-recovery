param([Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$tag = 'v' + $Version
$assemblyText = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\AssemblyInfo.cs') -Raw
$assemblyVersion = [regex]::Match($assemblyText, 'AssemblyVersion\("([^"]+)"\)').Groups[1].Value
$fileVersion = [regex]::Match($assemblyText, 'AssemblyFileVersion\("([^"]+)"\)').Groups[1].Value
$productVersion = [regex]::Match($assemblyText, 'AssemblyInformationalVersion\("([^"]+)"\)').Groups[1].Value
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\app.manifest') -Raw
if ($assemblyVersion -ne "$Version.0" -or $fileVersion -ne "$Version.0" -or $productVersion -ne $Version -or $manifest.assembly.assemblyIdentity.version -ne "$Version.0") { throw 'Release, EXE and manifest versions do not match.' }
$changelog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Raw -Encoding UTF8
$pattern = '(?ms)^## \[' + [regex]::Escape($Version) + '\] - (\d{4}-\d{2}-\d{2})\r?\n(.*?)(?=^## |\z)'
$entry = [regex]::Match($changelog, $pattern)
if (-not $entry.Success) { throw 'A dated changelog entry is required for the release version.' }
& (Join-Path $PSScriptRoot 'package.ps1') -Release
& (Join-Path $PSScriptRoot 'tests\run.ps1')
$folder = Join-Path $PSScriptRoot ('dist\releases\' + $tag)
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$assets = @(
    @(('NetworkRecovery-' + $tag + '.exe'), ('NetworkRecovery-' + $tag + '.exe')),
    @('NetworkRecovery-windows.zip', ('NetworkRecovery-' + $tag + '-windows.zip')),
    @('NetworkRecovery-source.zip', ('NetworkRecovery-' + $tag + '-source.zip'))
)
foreach ($asset in $assets) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('dist\' + $asset[0])) -Destination (Join-Path $folder $asset[1]) -Force }
$sums = foreach ($asset in $assets) { $hash = Get-FileHash -LiteralPath (Join-Path $folder $asset[1]) -Algorithm SHA256; $hash.Hash.ToLowerInvariant() + '  ' + $asset[1] }
$sums | Set-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt') -Encoding ASCII
$notes = @(
    ('# Network Recovery ' + $tag),
    '',
    'Windows 預發行版本，需要 .NET Framework 4.8 Runtime 與管理員權限。使用前請關閉睡眠與休眠。',
    '',
    ('EXE 產品版本：' + $Version + '；檔案／manifest 版本：' + $fileVersion + '。'),
    '',
    ($entry.Groups[2].Value.Trim()),
    '',
    '## 驗證與限制',
    '',
    '已通過文件檢查、Framework 建置、XML／啟動策略、示範 UI／選單、重啟額度、SSID／暫停／修復策略、隔離檔案權限、多程序視窗及嵌入圖標／透明度檢查。排程只做唯讀／TASK_VALIDATE_ONLY 驗證；既有受保護工作狀態只在提升權限時讀取。',
    '仍待實機驗收：開機觸發、跨 Windows 重啟額度、Wi-Fi 掃描與候選重連、暫停到期／取消、進階重設與備份還原、升級失敗還原及不同 VPN／驅動。地區程序保護只測試模擬查詢與動作，未以 SYSTEM 實際查詢地區或終止 Claude／cowork-svc。先前使用者回報的斷網修復／重啟與安裝錯誤修復，不代表此版本全部端到端驗收完成。',
    'EXE 尚未簽署。自動重啟會強制關閉程式，可能遺失未儲存工作。跨開機以 UTC 銜接時間；系統時間大幅前進可能提前釋放重啟額度。此版本仍保留預發行標示。',
    '自訂圖標已嵌入單一 EXE：應用／視窗為藍色、啟用監控為綠色、暫停／停止為灰色，支援 16～256 像素九種尺寸；不需另帶圖標檔。',
    '',
    '## 升級',
    '',
    '先關閉所有舊主設定視窗，執行新版 EXE 後按「儲存並啟用」才切換監控。程式會替換已知的 NetworkRecovery Monitor／Tray 排程，停止並停用早期 Codex Network Recovery 排程，失敗時嘗試還原。未知名稱的排程或手動啟動的舊腳本需要自行停止。',
    '',
    '## 下載',
    '',
    ('- NetworkRecovery-' + $tag + '.exe：單一應用程式。'),
    ('- NetworkRecovery-' + $tag + '-windows.zip：版本化 EXE、README 與 EXE 校驗檔。'),
    ('- NetworkRecovery-' + $tag + '-source.zip：原始碼、測試、文件與圖標素材，排除私人舊資料及狀態。'),
    '- SHA256SUMS.txt：上述三個附件的 SHA-256 校驗值。'
)
$notes -join "`n" | Set-Content -LiteralPath (Join-Path $folder 'RELEASE_NOTES.md') -Encoding UTF8
Write-Output ('Prepared release ' + $tag + ' in ' + $folder)

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
& (Join-Path $PSScriptRoot 'package.ps1')
& (Join-Path $PSScriptRoot 'tests\run.ps1')
$folder = Join-Path $PSScriptRoot ('dist\releases\' + $tag)
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$assets = @(
    @('NetworkRecovery.exe', ('NetworkRecovery-' + $tag + '.exe')),
    @('NetworkRecovery-windows.zip', ('NetworkRecovery-' + $tag + '-windows.zip')),
    @('NetworkRecovery-source.zip', ('NetworkRecovery-' + $tag + '-source.zip'))
)
foreach ($asset in $assets) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('dist\' + $asset[0])) -Destination (Join-Path $folder $asset[1]) -Force }
$sums = foreach ($asset in $assets) { $hash = Get-FileHash -LiteralPath (Join-Path $folder $asset[1]) -Algorithm SHA256; $hash.Hash.ToLowerInvariant() + '  ' + $asset[1] }
$sums | Set-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt') -Encoding ASCII
$notes = @(
    ('# Network Recovery ' + $tag),
    '',
    'First-stage preview release for Windows. Requires .NET Framework 4.8 and administrator permission.',
    '',
    ('EXE product version: ' + $Version + '; file version: ' + $fileVersion + '.'),
    '',
    ($entry.Groups[2].Value.Trim()),
    '',
    '## Validation and limitations',
    '',
    'Build, canonical-document checks, isolated Wi-Fi/input checks, a read-only missing-task regression check and demo-only UI layout checks passed. The installed-task status check is run only when elevated. Read-only adapter/Wi-Fi checks were completed on the development machine.',
    'Full end-to-end network repair and reboot testing of this EXE is still pending. The executable is unsigned. Automatic restarts can close unsaved work.',
    '',
    '## Downloads',
    '',
    '- EXE: standalone application. Run and accept the Windows administrator prompt.',
    '- Windows ZIP: EXE, README and checksum.',
    '- Source ZIP: source and project documents; excludes private legacy files and diagnostics.',
    '- SHA256SUMS.txt: SHA-256 digests of all three attached files.'
)
$notes -join "`n" | Set-Content -LiteralPath (Join-Path $folder 'RELEASE_NOTES.md') -Encoding UTF8
Write-Output ('Prepared release ' + $tag + ' in ' + $folder)

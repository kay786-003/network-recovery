$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$scripts = @('build.ps1', 'build-icons.ps1', 'package.ps1', 'release.ps1') | ForEach-Object { Join-Path $project $_ }
$scripts += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | ForEach-Object { $_.FullName })
$utf8 = New-Object Text.UTF8Encoding($false, $true)
foreach ($script in $scripts) {
    $bytes = [IO.File]::ReadAllBytes($script)
    $text = $utf8.GetString($bytes)
    $hasNonAscii = $text -match '[^\x00-\x7F]'
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
    if ($hasNonAscii -and -not $hasBom) {
        throw ('Windows PowerShell scripts containing non-ASCII text require a UTF-8 BOM: ' + [IO.Path]::GetFileName($script))
    }
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw ('PowerShell parse errors in: ' + [IO.Path]::GetFileName($script)) }
}
Write-Output ('PASS: ' + $scripts.Count + ' public PowerShell scripts use locale-safe UTF-8 encoding and parse successfully.')

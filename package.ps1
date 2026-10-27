param([switch]$Release)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'tests\documentation.ps1')
& (Join-Path $PSScriptRoot 'build.ps1') -Release:$Release
$dist = Join-Path $PSScriptRoot 'dist'
$sourceFiles = @('src','tests','docs','.github','.gitignore','.gitattributes','.editorconfig','AGENTS.md','README.md','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','NetworkRecovery.csproj','build.ps1','package.ps1','release.ps1') | ForEach-Object { Join-Path $PSScriptRoot $_ }
# Explicit allowlist prevents old local diagnostics/settings from entering a public package.
# Include the full registered docs/ set so the issue register ships in the source archive.
Compress-Archive -LiteralPath $sourceFiles -DestinationPath (Join-Path $dist 'NetworkRecovery-source.zip') -Force
$exeName = (Get-Content -LiteralPath (Join-Path $dist 'current-build.txt') -Raw).Trim()
$portableFiles = @((Join-Path $dist $exeName), (Join-Path $dist 'SHA256SUMS.txt'), (Join-Path $PSScriptRoot 'README.md'))
Compress-Archive -LiteralPath $portableFiles -DestinationPath (Join-Path $dist 'NetworkRecovery-windows.zip') -Force
Write-Output 'Created source and Windows EXE packages. legacy/ is excluded.'

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'tests\documentation.ps1')
& (Join-Path $PSScriptRoot 'build.ps1')
$dist = Join-Path $PSScriptRoot 'dist'
$sourceFiles = @('src','tests','docs','.github','.gitignore','.gitattributes','.editorconfig','AGENTS.md','README.md','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','NetworkRecovery.csproj','build.ps1','package.ps1','release.ps1') | ForEach-Object { Join-Path $PSScriptRoot $_ }
# Explicit allowlist prevents old local diagnostics/settings from entering a public package.
Compress-Archive -LiteralPath $sourceFiles -DestinationPath (Join-Path $dist 'NetworkRecovery-source.zip') -Force
$portableFiles = @((Join-Path $dist 'NetworkRecovery.exe'), (Join-Path $dist 'SHA256SUMS.txt'), (Join-Path $PSScriptRoot 'README.md'))
Compress-Archive -LiteralPath $portableFiles -DestinationPath (Join-Path $dist 'NetworkRecovery-windows.zip') -Force
Write-Output 'Created source and Windows EXE packages. legacy/ is excluded.'

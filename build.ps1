param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'), [switch]$Release)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 before building.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$assemblyText = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\AssemblyInfo.cs') -Raw
$productVersion = [regex]::Match($assemblyText, 'AssemblyInformationalVersion\("([^"]+)"\)').Groups[1].Value
if ($productVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid product version.' }
$suffix = if ($Release) { '' } else { '-dev-' + (Get-Date -Format 'yyyyMMdd-HHmmss') }
$fileName = 'NetworkRecovery-v' + $productVersion + $suffix + '.exe'
$destination = Join-Path $OutputDirectory $fileName
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output',('/out:' + $destination),('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll','/reference:System.Net.Http.dll','/reference:System.Management.dll','/reference:System.Security.dll','/reference:Microsoft.CSharp.dll')
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName })
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($destination).ProductVersion -ne $productVersion) { throw 'Built product version mismatch.' }
$fileName | Set-Content -LiteralPath (Join-Path $OutputDirectory 'current-build.txt') -Encoding ASCII
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
"$hash  $fileName" | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Built: $destination"

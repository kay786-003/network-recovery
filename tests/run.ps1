$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$folder = Join-Path ([IO.Path]::GetTempPath()) ('NetworkRecovery-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$exe = Join-Path $folder 'Checks.exe'
& $compiler /nologo /target:exe /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll ('/out:' + $exe) (Join-Path $project 'src\Models.cs') (Join-Path $project 'src\Networking.cs') (Join-Path $PSScriptRoot 'NetworkingPureChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
Write-Output 'No network configuration, profiles, tasks or restart actions were changed.'

# Read-only Windows COM regression: a missing task may throw FileNotFoundException.
$exeName = (Get-Content -LiteralPath (Join-Path $project 'dist\current-build.txt') -Raw).Trim()
if ($exeName -notmatch '^NetworkRecovery-v\d+\.\d+\.\d+(-dev-\d{8}-\d{6})?\.exe$') { throw 'Invalid build filename.' }
$application = Join-Path $project ('dist\' + $exeName)
if (-not (Test-Path -LiteralPath $application)) { throw 'Run build.ps1 before tests.' }
Copy-Item -LiteralPath $application -Destination $folder
$schedulerExe = Join-Path $folder 'SchedulerReadChecks.exe'
& $compiler /nologo /target:exe ('/reference:' + $application) ('/out:' + $schedulerExe) (Join-Path $PSScriptRoot 'SchedulerReadChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Scheduler test compilation failed.' }
& $schedulerExe
if ($LASTEXITCODE -ne 0) { throw 'Scheduler read-only tests failed.' }

# Demo-only layout regression: detect sibling occlusion as well as clipping.
$uiExe = Join-Path $folder 'UiLayoutChecks.exe'
& $compiler /nologo /target:exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ('/reference:' + $application) ('/out:' + $uiExe) (Join-Path $PSScriptRoot 'UiLayoutChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
& $uiExe
if ($LASTEXITCODE -ne 0) { throw 'UI layout tests failed.' }

# Isolated restart history checks; no production state or actions.
$budgetExe = Join-Path $folder 'RestartBudgetChecks.exe'
& $compiler /nologo /target:exe /reference:System.Xml.dll ('/out:' + $budgetExe) (Join-Path $project 'src\RestartBudget.cs') (Join-Path $PSScriptRoot 'RestartBudgetChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Restart budget test compilation failed.' }
& $budgetExe
if ($LASTEXITCODE -ne 0) { throw 'Restart budget tests failed.' }

$featuresExe = Join-Path $folder 'RecoveryFeatureChecks.exe'
& $compiler /nologo /target:exe /reference:System.Xml.dll ('/reference:' + $application) ('/out:' + $featuresExe) (Join-Path $PSScriptRoot 'RecoveryFeatureChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Recovery feature test compilation failed.' }
& $featuresExe
if ($LASTEXITCODE -ne 0) { throw 'Recovery feature tests failed.' }

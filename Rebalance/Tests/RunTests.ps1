param([string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Star Renegades',[string]$PythonExecutable='python')
$ErrorActionPreference='Stop'
$rebalanceRoot=Split-Path $PSScriptRoot -Parent
$project=Split-Path $rebalanceRoot -Parent
$generator=Join-Path $rebalanceRoot 'make_manifest.py'
if(-not (Test-Path -LiteralPath $generator)){throw 'FAIL manifest generator is missing.'}
$manifest=Join-Path $rebalanceRoot 'Source/rebalance.json'
$originalHash=(Get-FileHash -LiteralPath $manifest).Hash
& $PythonExecutable $generator
if($LASTEXITCODE -ne 0){throw 'Manifest generation failed.'}
if((Get-FileHash -LiteralPath $manifest).Hash -ne $originalHash){throw 'Manifest generation changed the reviewed output.'}
Write-Host 'PASS manifest generation is reproducible'
$fixtureRoot=Join-Path $rebalanceRoot ('bin/generator-fixtures-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $fixtureRoot 'Source'),(Join-Path $fixtureRoot 'Rebalance/Source') | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'Source/fixes.json') -Destination (Join-Path $fixtureRoot 'Source/fixes.json') -Force
Copy-Item -LiteralPath $generator -Destination (Join-Path $fixtureRoot 'Rebalance/make_manifest.py') -Force
$badSource=Get-Content -LiteralPath (Join-Path $rebalanceRoot 'balance-changes.json') -Raw | ConvertFrom-Json
$core=Get-Content -LiteralPath (Join-Path $project 'Source/fixes.json') -Raw | ConvertFrom-Json
$badSource.changes[0]=$core.changes[0]
[IO.File]::WriteAllText((Join-Path $fixtureRoot 'Rebalance/balance-changes.json'),($badSource | ConvertTo-Json -Depth 32))
& $PythonExecutable (Join-Path $fixtureRoot 'Rebalance/make_manifest.py') 2>&1 | Out-Null
if($LASTEXITCODE -eq 0){throw 'Generator accepted a field overlapping shared fixes.'}
if(Test-Path -LiteralPath (Join-Path $fixtureRoot 'Rebalance/Source/rebalance.json')){throw 'Rejected generation wrote an output manifest.'}
Write-Host 'PASS generator rejects overlap before writing'
& (Join-Path $rebalanceRoot 'build.ps1') -GamePath $GamePath -Tests Core
& (Join-Path $rebalanceRoot 'build.ps1') -GamePath $GamePath -Tests Activation

param([Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Star Renegades',[ValidateSet('SRBugfix','SRRebalance')][string]$Mod='SRRebalance')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$managed=Join-Path $GamePath 'Star Renegades_Data/Managed'
$module=Join-Path (Join-Path $GamePath $Mod) ($Mod+'.dll')
if(-not (Test-Path -LiteralPath $module)){throw 'Install the candidate mod before preparing its loading check.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sdkLine=(& dotnet --list-sdks | Select-Object -Last 1)
if($sdkLine -notmatch '^([^ ]+) \[(.+)\]$'){throw 'A .NET SDK is required.'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$options=@('/nologo','/nostdlib+','/langversion:7.3','/target:library','/deterministic+',('/out:"'+(Join-Path $output 'LoadingDiagnostic.dll')+'"'))
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
$options+=('/reference:"'+(Join-Path $project 'dependencies/0Harmony.dll')+'"')
$options+=('"'+(Join-Path $PSScriptRoot 'LoadingDiagnostic.cs')+'"')
$response=Join-Path $output 'diagnostic.rsp';[IO.File]::WriteAllLines($response,$options)
& dotnet $compiler ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Loading diagnostic compilation failed.'}
$config=Join-Path $GamePath 'doorstop_config.ini'
if((Get-Content -LiteralPath $config -Raw) -notmatch [regex]::Escape('target_assembly='+$Mod+'/'+$Mod+'.dll')){throw 'The selected mod is not the active startup target.'}
$settings=@{gameRoot=[IO.Path]::GetFullPath($GamePath);module=$module;moduleHash=(Get-FileHash -LiteralPath $module).Hash.ToLowerInvariant();configHash=(Get-FileHash -LiteralPath $config).Hash.ToLowerInvariant();diagnosticHash=(Get-FileHash -LiteralPath (Join-Path $output 'LoadingDiagnostic.dll')).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText((Join-Path $output 'settings.json'),($settings|ConvertTo-Json))
[IO.File]::WriteAllLines((Join-Path $output 'test-paths.txt'),@($settings.gameRoot,(Split-Path $module -Parent)))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LoadingCheckLauncher.ps1') -Destination (Join-Path $output 'LoadingCheckLauncher.ps1') -Force
[IO.File]::WriteAllText((Join-Path $output 'Start loading check.cmd'),"@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0LoadingCheckLauncher.ps1`"`r`nif errorlevel 1 pause`r`n")
[IO.File]::WriteAllText((Join-Path $output 'Restore normal startup.cmd'),"@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0LoadingCheckLauncher.ps1`" -Restore`r`nif errorlevel 1 pause`r`n")
Write-Host ('Prepared optional loading check: '+$output)

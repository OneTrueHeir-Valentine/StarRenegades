param([string]$LauncherPath=(Join-Path $PSScriptRoot 'LoadingCheckLauncher.ps1'))
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$fixtureRoot=Join-Path $project ('artifacts/loading launcher '+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
$config=Join-Path $fixtureRoot 'doorstop_config.ini'
$original=[Text.Encoding]::UTF8.GetBytes("[General]`r`nenabled=true`r`ntarget_assembly=SRRebalance/SRRebalance.dll`r`n")
[IO.File]::WriteAllBytes($config,$original)
foreach($name in @('module.bin','LoadingDiagnostic.dll')){[IO.File]::WriteAllText((Join-Path $fixtureRoot $name),'inert fixture; never loaded')}
$settings=@{gameRoot=$fixtureRoot;module=(Join-Path $fixtureRoot 'module.bin');configHash=(Get-FileHash -LiteralPath $config).Hash.ToLowerInvariant();moduleHash=(Get-FileHash -LiteralPath (Join-Path $fixtureRoot 'module.bin')).Hash.ToLowerInvariant();diagnosticHash=(Get-FileHash -LiteralPath (Join-Path $fixtureRoot 'LoadingDiagnostic.dll')).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText((Join-Path $fixtureRoot 'settings.json'),($settings|ConvertTo-Json))
$launcher=Join-Path $fixtureRoot 'LoadingCheckLauncher.ps1'
Copy-Item -LiteralPath $LauncherPath -Destination $launcher
& $launcher -PrepareOnly -NoElevation
$changed=Get-Content -LiteralPath $config -Raw
if($changed -notmatch 'enabled=true' -or $changed -notmatch [regex]::Escape('target_assembly='+(Join-Path $fixtureRoot 'LoadingDiagnostic.dll'))){throw 'Launcher did not set its isolated target.'}
Write-Host 'PASS diagnostic preparation changes only the fixture startup target'
& $launcher -Restore -NoElevation
if([Convert]::ToBase64String([IO.File]::ReadAllBytes($config)) -ne [Convert]::ToBase64String($original)){throw 'Restore did not retain exact original bytes.'}
Write-Host 'PASS restoration preserves original configuration bytes'
& $launcher -PrepareOnly -NoElevation
$session=Get-Content -LiteralPath (Join-Path $fixtureRoot 'session.json') -Raw | ConvertFrom-Json
[IO.File]::WriteAllText($config,'external change')
$rejected=$false
try{& $launcher -Restore -NoElevation}catch{$rejected=$true}
if(-not $rejected -or [IO.File]::ReadAllText($config) -ne 'external change' -or -not (Test-Path -LiteralPath $session.backup)){throw 'Restore overwrote an outside change or lost its backup.'}
Write-Host 'PASS changed configuration and original backup are preserved on conflict'
# Execute the actual atomic-writer function with a stale observed hash,
# modelling an edit after the caller's hash check and before replacement.
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($launcher,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'Launcher parsing failed.'}
$writer=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-StartupConfig'},$true).Body.GetScriptBlock()
$oldHash=(Get-FileHash -LiteralPath $config).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($config,'external edit after hash check')
$rejected=$false
try{& $writer $original $oldHash}catch{$rejected=$true}
$displacedFiles=Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.loading-displaced.*.bak'
if(-not $rejected -or $displacedFiles.Count -ne 1 -or [IO.File]::ReadAllText($displacedFiles[0].FullName) -ne 'external edit after hash check'){throw 'Replacement race lost displaced bytes.'}
Write-Host 'PASS edits during atomic replacement retain displaced configuration bytes'
# Keep the argument validation of Start-Process, replacing its UAC side effect.
$loadingCheckCapture=[pscustomobject]@{Arguments=$null}
function Start-Process {
 param([string]$FilePath,[string]$Verb,[ValidateNotNullOrEmpty()][string[]]$ArgumentList)
 $loadingCheckCapture.Arguments=$ArgumentList
}
function New-Object {
 param([string]$TypeName,$ArgumentList)
 if($TypeName -ne 'Security.Principal.WindowsPrincipal'){throw ('Unexpected constructor in elevation fixture: '+$TypeName)}
 $principal=[pscustomobject]@{}
 $principal | Add-Member -MemberType ScriptMethod -Name IsInRole -Value {param($role)return $false}
 return $principal
}
try{
 foreach($mode in @('Normal','Restore','PrepareOnly')){
  $loadingCheckCapture.Arguments=$null
  if($mode -eq 'Restore'){& $launcher -Restore}elseif($mode -eq 'PrepareOnly'){& $launcher -PrepareOnly}else{& $launcher}
  $actual=$loadingCheckCapture.Arguments
  $expectedCount=if($mode -eq 'Normal'){5}else{6}
  if($actual.Count -ne $expectedCount -or @($actual | Where-Object {[string]::IsNullOrEmpty($_)}).Count){throw ('Invalid administrator launch arguments for '+$mode)}
  if($actual[0] -ne '-NoProfile' -or $actual[3] -ne '-File' -or $actual[4] -cne ('"'+$launcher+'"')){throw 'Administrator relaunch lost its script argument or quoting.'}
  if($mode -ne 'Normal' -and $actual[5] -ne ('-'+$mode)){throw 'Administrator relaunch lost its requested mode.'}
  Write-Host ('PASS '+$mode+' administrator relaunch passes only populated arguments')
 }
}finally{Remove-Item -LiteralPath Function:\Start-Process;Remove-Item -LiteralPath Function:\New-Object}
Write-Host 'RESULT 7 passed, 0 failed; inert workspace files only, no Steam or administrator process launch'

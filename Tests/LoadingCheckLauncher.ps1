param([switch]$PrepareOnly,[switch]$Restore,[switch]$NoElevation)
$ErrorActionPreference='Stop'
$settings=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'settings.json') -Raw | ConvertFrom-Json
$gameRoot=[IO.Path]::GetFullPath($settings.gameRoot)
$config=Join-Path $gameRoot 'doorstop_config.ini'
$sessionFile=Join-Path $PSScriptRoot 'session.json'
function Write-StartupConfig {
 param([byte[]]$bytes,[string]$expectedHash)
 $staging=$config+'.loading-check.'+[Guid]::NewGuid().ToString('N')+'.tmp'
 $displaced=$config+'.loading-displaced.'+[Guid]::NewGuid().ToString('N')+'.bak'
 [IO.File]::WriteAllBytes($staging,$bytes)
 [IO.File]::Replace($staging,$config,$displaced)
 if((Get-FileHash -LiteralPath $displaced).Hash.ToLowerInvariant() -ne $expectedHash){throw ('Startup changed during replacement. Its displaced bytes are preserved at '+$displaced+'. Do not launch until that configuration is reviewed.')}
 Remove-Item -LiteralPath $displaced
}
if(-not $NoElevation){
 $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
 if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
  $extra=if($Restore){'-Restore'}elseif($PrepareOnly){'-PrepareOnly'}else{''}
  Start-Process -FilePath powershell.exe -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),$extra)
  return
 }
}
function Restore-NormalConfig {
 if(-not (Test-Path -LiteralPath $sessionFile)){return}
 $savedSession=Get-Content -LiteralPath $sessionFile -Raw | ConvertFrom-Json
 if([IO.Path]::GetFullPath($savedSession.config) -ne $config){throw 'The recovery record belongs to a different game folder.'}
 $currentHash=(Get-FileHash -LiteralPath $config).Hash.ToLowerInvariant()
 $original=[IO.File]::ReadAllBytes($savedSession.backup)
 $backupHash=(Get-FileHash -LiteralPath $savedSession.backup).Hash.ToLowerInvariant()
 if($backupHash -ne $savedSession.originalHash){throw 'The saved startup configuration changed. It was not restored.'}
 if($currentHash -eq $savedSession.testHash){Write-StartupConfig $original $savedSession.testHash}
 elseif($currentHash -ne $savedSession.originalHash){throw ('Startup configuration changed outside this check. Preserved it and the backup at '+$savedSession.backup)}
 Write-Host 'Normal startup configuration restored. The next game launch uses the normal mod.'
}
if($Restore){Restore-NormalConfig;return}
if(Get-Process -Name 'Star Renegades' -ErrorAction SilentlyContinue){throw 'Close Star Renegades before starting the loading check.'}
Restore-NormalConfig
if((Get-FileHash -LiteralPath $config).Hash.ToLowerInvariant() -ne $settings.configHash){throw 'Startup configuration changed since this check was prepared. It was not modified.'}
if((Get-FileHash -LiteralPath $settings.module).Hash.ToLowerInvariant() -ne $settings.moduleHash){throw 'The installed mod differs from the prepared loading check. Rebuild the check first.'}
$diagnostic=Join-Path $PSScriptRoot 'LoadingDiagnostic.dll'
if((Get-FileHash -LiteralPath $diagnostic).Hash.ToLowerInvariant() -ne $settings.diagnosticHash){throw 'Loading diagnostic checksum mismatch.'}
$originalBytes=[IO.File]::ReadAllBytes($config)
$text=[Text.Encoding]::UTF8.GetString($originalBytes)
if([regex]::Matches($text,'(?m)^target_assembly=[^\r\n]*').Count -ne 1){throw 'Expected one Doorstop startup target.'}
$targetLine='target_assembly='+$diagnostic
$testText=[regex]::Replace($text,'(?m)^target_assembly=[^\r\n]*',{param($match)$targetLine})
$backup=Join-Path $PSScriptRoot ('original-config.'+[Guid]::NewGuid().ToString('N')+'.ini')
[IO.File]::WriteAllBytes($backup,$originalBytes)
$nonce=[Guid]::NewGuid().ToString('N')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'diagnostic-nonce.txt'),$nonce)
$testBytes=(New-Object Text.UTF8Encoding($false)).GetBytes($testText)
$sha=[Security.Cryptography.SHA256]::Create()
try{$testHash=[BitConverter]::ToString($sha.ComputeHash($testBytes)).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
$record=@{config=$config;backup=$backup;originalHash=(Get-FileHash -LiteralPath $backup).Hash.ToLowerInvariant();testHash=$testHash}
[IO.File]::WriteAllText($sessionFile,($record|ConvertTo-Json))
Write-StartupConfig $testBytes $settings.configHash
if($PrepareOnly){return}
try{
 Write-Host 'This check makes loading fail on purpose, before reading the selected save.'
 Write-Host 'In the game, choose an existing save. Confirm or Back should close the error message and return to the save list.'
 Write-Host 'Close the game after this check. Your next launch will load normally.'
 Start-Process 'steam://rungameid/651670'
 $deadline=[DateTime]::UtcNow.AddSeconds(120)
 $ack=Join-Path $PSScriptRoot 'diagnostic-started.txt'
 while(-not ((Test-Path -LiteralPath $ack) -and (Get-Content -LiteralPath $ack -Raw) -eq $nonce)){
  if([DateTime]::UtcNow -gt $deadline){throw 'The diagnostic did not confirm startup. Normal startup will be restored.'}
  Start-Sleep -Milliseconds 500
 }
}finally{Restore-NormalConfig}
Read-Host 'The loading check is ready in the game. Press Enter to close this window'

param([string]$GameRoot,[switch]$Elevated)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Support.ps1')
try {
 if(-not $GameRoot){$GameRoot=Select-SRGame}
 $GameRoot=Get-SRRoot $GameRoot;Assert-SRStopped $GameRoot
 $payload=Split-Path $PSScriptRoot -Parent
 if(-not(Test-Path -LiteralPath (Join-Path $GameRoot '.SRBugfix-transaction'))){Get-SRBugfixPlan $GameRoot $payload|Out-Null}
 $probe=Get-SRPath $GameRoot ('.SRBugfix-access-'+[Guid]::NewGuid().ToString('N'))
 try{[IO.File]::WriteAllText($probe,'');Remove-Item -LiteralPath $probe -Force}
 catch [UnauthorizedAccessException] {
  if($Elevated){throw}
  $scriptLiteral=$PSCommandPath.Replace("'","''");$rootLiteral=$GameRoot.Replace("'","''")
  $command="& '$scriptLiteral' -GameRoot '$rootLiteral' -Elevated"
  $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
  $process=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) -Verb RunAs -Wait -PassThru
  exit $process.ExitCode
 }
 Install-SRBugfix $GameRoot $payload
 Write-Host 'Installed Star Renegades Bugfix Mod. Start the game normally through Steam.'
 exit 0
}catch{Write-Host ('Installation stopped: '+$_.Exception.Message) -ForegroundColor Red;exit 1}

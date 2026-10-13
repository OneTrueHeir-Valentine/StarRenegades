param([string]$GameRoot,[switch]$Elevated,[ValidateSet('SRBugfix','SRRebalance')][string]$ModId='SRBugfix')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Support.ps1') -ModId $ModId
try {
 if(-not $GameRoot){$GameRoot=Select-SRGame}
 $GameRoot=Get-SRRoot $GameRoot;Assert-SRStopped $GameRoot
 if(-not(Test-Path -LiteralPath (Join-Path $GameRoot $script:JournalDirectory))){$receipt=Get-SRReceipt $GameRoot;if($null -eq $receipt){throw "No $($script:ModName) installation receipt was found."}}
 $probe=Get-SRPath $GameRoot (('.'+$ModId+'-access-')+[Guid]::NewGuid().ToString('N'))
 try{[IO.File]::WriteAllText($probe,'');Remove-Item -LiteralPath $probe -Force}
 catch [UnauthorizedAccessException] {
  if($Elevated){throw}
  $scriptLiteral=$PSCommandPath.Replace("'","''");$rootLiteral=$GameRoot.Replace("'","''")
  $command="& '$scriptLiteral' -GameRoot '$rootLiteral' -ModId '$ModId' -Elevated"
  $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
  $process=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) -Verb RunAs -Wait -PassThru
  exit $process.ExitCode
 }
 Uninstall-SRBugfix $GameRoot
 Write-Host "Uninstalled Star Renegades $($script:ModName). Save files and logs were kept."
 exit 0
}catch{Write-Host ('Uninstall stopped: '+$_.Exception.Message) -ForegroundColor Red;exit 1}

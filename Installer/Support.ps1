Set-StrictMode -Version 2
$script:ExpectedGame=@{
 'Star Renegades_Data/Managed/Assembly-CSharp.dll'='a9d49075bab3953b7748b7e91d94829d325580e870297df8ebdc8d43f55427d0'
 'Star Renegades_Data/data.unity3d'='4c405e4f179f475a67e8561a5163bddb55e7e6340de83bb876ebb1f94c44275d'
}
$script:OwnedPaths=@('winhttp.dll','doorstop_config.ini','SRBugfix/SRBugfix.dll','SRBugfix/0Harmony.dll','SRBugfix/fixes.json')
$script:WriteInjection=$null;$script:ProcessInjection=$null;$script:SkipProcessForFixtures=$false;$script:InterruptForFixtures=0
function Hash-Bytes([byte[]]$Bytes){$sha=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}}
function Get-SRRoot([string]$Path){
 $root=[IO.Path]::GetFullPath($Path).TrimEnd('\','/')
 if(-not(Test-Path -LiteralPath (Join-Path $root 'Star Renegades.exe') -PathType Leaf)){throw 'Select the folder containing Star Renegades.exe.'}
 $node=Get-Item -LiteralPath $root
 while($null -ne $node){if($node.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Choose the actual Steam library folder rather than a junction or symbolic link.'};$node=$node.Parent}
 return $root
}
function Get-SRPath([string]$Root,[string]$Relative){
 if([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[/\\])\.\.([/\\]|$)' -or $Relative -match '[:\x00]'){throw 'Invalid relative file path.'}
 $full=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
 if(-not $full.StartsWith($Root.TrimEnd('\','/')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'File path leaves the game folder.'}
 $node=$full
 while($node.Length -gt $Root.Length){if(Test-Path -LiteralPath $node){$item=Get-Item -LiteralPath $node -Force;if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'A mod file path contains a junction or symbolic link.'}};$node=[IO.Path]::GetDirectoryName($node)}
 return $full
}
function Assert-SRStopped([string]$Root){
 if($script:ProcessInjection){if(& $script:ProcessInjection $Root){throw 'Close Star Renegades before changing the mod.'};return}
 if($script:SkipProcessForFixtures){return}
 foreach($process in @(Get-Process -Name 'Star Renegades' -ErrorAction SilentlyContinue)){
  try{$exe=$process.MainModule.FileName}catch{throw 'Star Renegades is running. Close it before changing the mod.'}
  if($exe -eq (Join-Path $Root 'Star Renegades.exe')){throw 'Close Star Renegades before changing the mod.'}
 }
}
function Read-SRJson([string]$Path){try{return [IO.File]::ReadAllText($Path)|ConvertFrom-Json}catch{throw "Cannot read $([IO.Path]::GetFileName($Path))."}}
function Get-SRBytes([string]$Path){if(Test-Path -LiteralPath $Path -PathType Leaf){return ,([IO.File]::ReadAllBytes($Path))};if(Test-Path -LiteralPath $Path){throw 'A file path is occupied by a directory.'};return $null}
function Set-SRBytes([string]$Path,$Bytes){
 if($null -eq $Bytes){if(Test-Path -LiteralPath $Path){Remove-Item -LiteralPath $Path -Force};return}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))|Out-Null
 [IO.File]::WriteAllBytes($Path,[byte[]]$Bytes)
}
function Write-SRAtomicJson([string]$Path,$Value){$tmp=$Path+'.new';[IO.File]::WriteAllText($tmp,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false));Move-Item -LiteralPath $tmp -Destination $Path -Force}
function Get-SRReceipt([string]$Root){
 $path=Get-SRPath $Root 'SRBugfix/receipt.json';if(-not(Test-Path -LiteralPath $path)){return $null}
 $receipt=Read-SRJson $path
 if($receipt.format -ne 1 -or $receipt.id -ne 'SRBugfix' -or @($receipt.files).Count -ne $script:OwnedPaths.Count){throw 'Unrecognized installation receipt.'}
 $seen=@{}
 foreach($file in $receipt.files){if($file.path -notin $script:OwnedPaths -or $seen.ContainsKey($file.path) -or $file.hash -notmatch '^[a-f0-9]{64}$'){throw 'Invalid installation receipt.'};$seen[$file.path]=$true
  $dest=Get-SRPath $Root $file.path;$bytes=Get-SRBytes $dest
  if($null -eq $bytes -or (Hash-Bytes $bytes) -ne $file.hash){throw "The installed file $($file.path) was changed. It has been preserved. Restore the released file before updating or uninstalling."}
  if($null -ne $file.original){try{[Convert]::FromBase64String($file.original)|Out-Null}catch{throw 'Invalid uninstall backup.'}}
 }
 return $receipt
}
function Clear-SRJournal([string]$Root){
 $dir=Get-SRPath $Root '.SRBugfix-transaction'
 if(Test-Path -LiteralPath $dir){foreach($file in @(Get-ChildItem -LiteralPath $dir -Force)){if($file.PSIsContainer){throw 'Unexpected transaction directory.'};Get-SRPath $Root ('.SRBugfix-transaction/'+$file.Name)|Out-Null;Remove-Item -LiteralPath $file.FullName -Force};Remove-Item -LiteralPath $dir -Force}
}
function Recover-SRTransaction([string]$Root){
 $dir=Get-SRPath $Root '.SRBugfix-transaction';if(-not(Test-Path -LiteralPath $dir)){return}
 $journalPath=Get-SRPath $Root '.SRBugfix-transaction/journal.json'
 if(-not(Test-Path -LiteralPath $journalPath)){throw 'An incomplete transaction has no journal. Preserve it and report the problem.'}
 $journal=Read-SRJson $journalPath
 if($journal.format -ne 1 -or @($journal.files).Count -ne 6){throw 'Invalid recovery journal.'}
 $seen=@{};$recovery=@()
 foreach($file in $journal.files){
  if($file.path -notin ($script:OwnedPaths+@('SRBugfix/receipt.json')) -or $seen.ContainsKey($file.path)){throw 'Invalid recovery file path.'};$seen[$file.path]=$true
  $dest=Get-SRPath $Root $file.path;$old=if($null -eq $file.before){$null}else{[Convert]::FromBase64String($file.before)}
  $present=Get-SRBytes $dest;$hash=if($null -eq $present){'absent'}else{Hash-Bytes $present};$beforeHash=if($null -eq $old){'absent'}else{Hash-Bytes $old}
  if($hash -ne $beforeHash -and $hash -ne $file.afterHash){throw "Recovery stopped because $($file.path) was changed. Your file has been preserved."}
  $recovery+=@{path=$dest;bytes=$old}
 }
 foreach($file in $recovery){Set-SRBytes $file.path $file.bytes};Clear-SRJournal $Root
}
function Invoke-SRTransaction([string]$Root,$Changes){
 $files=@();foreach($change in $Changes){$dest=Get-SRPath $Root $change.path;$before=Get-SRBytes $dest;$files+=@{path=$change.path;before=if($null -eq $before){$null}else{[Convert]::ToBase64String($before)};afterHash=if($null -eq $change.bytes){'absent'}else{Hash-Bytes $change.bytes}}}
 $dir=Get-SRPath $Root '.SRBugfix-transaction';[IO.Directory]::CreateDirectory($dir)|Out-Null
 Write-SRAtomicJson (Join-Path $dir 'journal.json') @{format=1;files=$files}
 $count=0
 try{
  foreach($change in $Changes){$dest=Get-SRPath $Root $change.path
   if($script:WriteInjection -and $null -ne $change.bytes){[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest))|Out-Null;& $script:WriteInjection $dest $change.bytes}else{Set-SRBytes $dest $change.bytes}
   $actual=Get-SRBytes $dest;$expected=$files[$count].afterHash;$actualHash=if($null -eq $actual){'absent'}else{Hash-Bytes $actual};if($expected -ne $actualHash){throw 'Copied file failed verification.'}
   $count++;if($script:InterruptForFixtures -eq $count){throw 'Fixture interrupt'}
  }
  Clear-SRJournal $Root
 }catch{
  if($script:InterruptForFixtures -eq 0){foreach($file in $files){$bytes=if($null -eq $file.before){$null}else{[Convert]::FromBase64String($file.before)};Set-SRBytes (Get-SRPath $Root $file.path) $bytes};Clear-SRJournal $Root}
  throw
 }
}
function Get-SRBugfixPlan([string]$GameRoot,[string]$PayloadRoot){
 $root=Get-SRRoot $GameRoot;Assert-SRStopped $root
 foreach($relative in $script:ExpectedGame.Keys){$path=Get-SRPath $root $relative;if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $script:ExpectedGame[$relative]){throw 'This release supports the unmodified Windows Steam build 1.5.1.5.'}}
 $payload=[IO.Path]::GetFullPath($PayloadRoot).TrimEnd('\','/');$package=Read-SRJson (Join-Path $payload 'package.json')
 if($package.format -ne 1 -or $package.version -notmatch '^\d+\.\d+\.\d+$' -or @($package.files.PSObject.Properties).Count -ne $script:OwnedPaths.Count){throw 'Invalid release package.'}
 $changes=@();$files=@();$receipt=Get-SRReceipt $root
 if($null -eq $receipt){foreach($relative in @('winhttp.dll','doorstop_config.ini','SRBugfix','SRRebalance','BepInEx')){$existing=Get-SRPath $root $relative;if(Test-Path -LiteralPath $existing){
  if($relative -eq 'SRBugfix' -and (Test-Path -LiteralPath $existing -PathType Container) -and @((Get-ChildItem -LiteralPath $existing -Force)|Where-Object {$_.Name -ne 'bugfix.log' -or $_.PSIsContainer}).Count -eq 0){continue}
  throw "An existing loader or mod ($relative) was found. Remove it using its own instructions first."
 }}}
 foreach($relative in $script:OwnedPaths){$prop=$package.files.PSObject.Properties[$relative];if($null -eq $prop -or $prop.Value -notmatch '^[a-f0-9]{64}$'){throw 'Invalid package file list.'};$source=Get-SRPath $payload $relative;$bytes=Get-SRBytes $source
  if($null -eq $bytes -or (Hash-Bytes $bytes) -ne $prop.Value){throw "Release file failed verification: $relative"}
  $original=$null;if($null -ne $receipt){$original=($receipt.files|Where-Object path -eq $relative).original}
  $files+=@{path=$relative;hash=$prop.Value;original=$original};$changes+=@{path=$relative;bytes=$bytes}
 }
 $newReceipt=@{format=1;id='SRBugfix';version=$package.version;files=$files}
 $changes+=@{path='SRBugfix/receipt.json';bytes=[Text.Encoding]::UTF8.GetBytes(($newReceipt|ConvertTo-Json -Depth 20))}
 return @{root=$root;changes=$changes}
}
function Invoke-SRLocked([string]$Root,[scriptblock]$Action){
 $key=Hash-Bytes ([Text.Encoding]::UTF8.GetBytes($Root.ToLowerInvariant()));$mutex=[Threading.Mutex]::new($false,('Local\SRBugfix-'+$key));$locked=$false
 try{try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$locked=$true};if(-not $locked){throw 'Another mod installer is already running.'};& $Action}finally{if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
function Install-SRBugfix([string]$GameRoot,[string]$PayloadRoot){
 $root=Get-SRRoot $GameRoot;Invoke-SRLocked $root {Assert-SRStopped $root;Recover-SRTransaction $root;$plan=Get-SRBugfixPlan $root $PayloadRoot;Invoke-SRTransaction $root $plan.changes}
}
function Uninstall-SRBugfix([string]$GameRoot){
 $root=Get-SRRoot $GameRoot;Invoke-SRLocked $root {Assert-SRStopped $root;Recover-SRTransaction $root;$receipt=Get-SRReceipt $root;if($null -eq $receipt){throw 'No Bugfix Mod installation receipt was found.'}
  $changes=@();foreach($file in $receipt.files){$bytes=if($null -eq $file.original){$null}else{[Convert]::FromBase64String($file.original)};$changes+=@{path=$file.path;bytes=$bytes}}
  $changes+=@{path='SRBugfix/receipt.json';bytes=$null};Invoke-SRTransaction $root $changes
 }
}
function Find-SRGame([string[]]$SteamRoots) {
 if(-not $SteamRoots){$SteamRoots=@();foreach($key in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')){if(Test-Path $key){$props=Get-ItemProperty $key;$property=$props.PSObject.Properties['SteamPath'];if($null -eq $property){$property=$props.PSObject.Properties['InstallPath']};if($null -ne $property){$SteamRoots+=$property.Value}}}}
 $libraries=@($steamRoots)
 foreach($root in $steamRoots){$vdf=Join-Path $root 'steamapps/libraryfolders.vdf';if(Test-Path -LiteralPath $vdf){$content=[IO.File]::ReadAllText($vdf);foreach($match in [regex]::Matches($content,'"(?:path|\d+)"\s*"([^"\r\n]+)"')){$candidate=$match.Groups[1].Value.Replace('\\','\');if([IO.Path]::IsPathRooted($candidate)){$libraries+=$candidate}}}}
 $found=@();foreach($library in @($libraries|Select-Object -Unique)){$manifest=Join-Path $library 'steamapps/appmanifest_651670.acf';if(Test-Path -LiteralPath $manifest){$text=[IO.File]::ReadAllText($manifest);$match=[regex]::Match($text,'"installdir"\s*"([^"\r\n]+)"');if($match.Success){$dir=Join-Path $library ('steamapps/common/'+$match.Groups[1].Value);try{$found+=Get-SRRoot $dir}catch{}}}}
 return @($found|Select-Object -Unique)
}
function Select-SRGame {
 $found=@(Find-SRGame);if($found.Count -eq 1){return $found[0]}
 Add-Type -AssemblyName System.Windows.Forms;$dialog=[Windows.Forms.FolderBrowserDialog]::new();$dialog.Description='Select the Star Renegades folder containing Star Renegades.exe.'
 try{if($dialog.ShowDialog() -ne [Windows.Forms.DialogResult]::OK){throw 'Folder selection cancelled.'};return Get-SRRoot $dialog.SelectedPath}finally{$dialog.Dispose()}
}

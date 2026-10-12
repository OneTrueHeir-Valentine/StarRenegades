$ErrorActionPreference='Stop'
$deps=Join-Path $PSScriptRoot 'dependencies';[IO.Directory]::CreateDirectory($deps)|Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
function Fetch-Entry($Url,$Entry,$Destination,$Hash){
 $archive=Join-Path $deps ([Guid]::NewGuid().ToString('N')+'.zip')
 try{Invoke-WebRequest -UseBasicParsing $Url -OutFile $archive;$zip=[IO.Compression.ZipFile]::OpenRead($archive)
  try{$file=$zip.GetEntry($Entry);if($null -eq $file){throw 'Dependency archive entry missing.'};[IO.Compression.ZipFileExtensions]::ExtractToFile($file,$Destination,$true)}finally{$zip.Dispose()}
  if((Get-FileHash -LiteralPath $Destination).Hash.ToLowerInvariant() -ne $Hash){throw 'Downloaded dependency hash mismatch.'}
 }finally{if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive -Force}}
}
Fetch-Entry 'https://github.com/pardeike/Harmony/releases/download/v2.2.2.0/Harmony.2.2.2.0.zip' 'net35/0Harmony.dll' (Join-Path $deps '0Harmony.dll') '9611251f080e4855cd9d5fa54b5e56034df25b70b374c49ba09550b08a1bf875'
Fetch-Entry 'https://github.com/NeighTools/UnityDoorstop/releases/download/v4.0.0/doorstop_win_release_4.0.0.zip' 'x64/winhttp.dll' (Join-Path $deps 'winhttp.dll') '7e90398dd7154b41f76e0524aedb795a7bcbe2ed9a14e4ceee214235aa04e442'
Write-Host 'Verified Harmony 2.2.2 net35 and Doorstop 4.0.0 x64.'

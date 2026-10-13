param([string]$Tests,[string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Star Renegades')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$managed=Join-Path $GamePath 'Star Renegades_Data/Managed'
$source=Join-Path $PSScriptRoot 'Source';$coreSource=Join-Path $project 'Source';$bin=Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$harmony=Join-Path $project 'dependencies/0Harmony.dll'
$sdkLine=(& dotnet --list-sdks | Select-Object -Last 1)
if($sdkLine -notmatch '^([^ ]+) \[(.+)\]$'){throw 'Install a .NET SDK to build the mod.'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$balanceHash=(Get-FileHash -LiteralPath (Join-Path $source 'rebalance.json')).Hash.ToLowerInvariant()
$coreHash=(Get-FileHash -LiteralPath (Join-Path $coreSource 'fixes.json')).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $source 'BuiltManifest.cs'),'namespace SRRebalance { internal static class BuiltManifest { internal const string Sha256="'+$balanceHash+'"; } }')
[IO.File]::WriteAllText((Join-Path $source 'SharedBuiltManifest.cs'),'namespace SRBugfix { internal static class BuiltManifest { internal const string Sha256="'+$coreHash+'"; } }')
$options=@(('/pathmap:"'+$project+'=/src"'),'/nologo','/nostdlib+','/langversion:7.3','/optimize+','/deterministic+')
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','Newtonsoft.Json.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
$options+=('/reference:"'+$harmony+'"');Copy-Item -LiteralPath $harmony -Destination (Join-Path $bin '0Harmony.dll') -Force
$files=@(Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object FullName)
$files+=@(Get-ChildItem -LiteralPath $coreSource -Filter '*.cs' | Where-Object Name -NotIn @('Entrypoint.cs','RuntimeProbe.cs','BuiltManifest.cs') | ForEach-Object FullName)
if(-not $Tests){$out=Join-Path $bin 'SRRebalance.dll';$options+=@('/target:library',('/out:"'+$out+'"'))}
else{
 $mains=@{Core='RebalanceCoreTests';Activation='RebalanceActivationTests'}
 if(-not $mains.ContainsKey($Tests)){throw 'Choose -Tests Core or Activation.'}
 $main=$mains[$Tests];$out=Join-Path $bin ($main+'.exe');$options+=@('/target:exe',('/main:'+$main),('/out:"'+$out+'"'))
 $files+=Join-Path $PSScriptRoot ('Tests/'+$main+'.cs')
 if($Tests -eq 'Activation'){$files+=Join-Path $project 'Tests/CoreTests.cs'}
}
foreach($file in $files){$options+=('"'+$file+'"')}
$rsp=Join-Path $bin 'compile.rsp';[IO.File]::WriteAllLines($rsp,$options)
& dotnet $compiler ('@'+$rsp)
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
if($Tests){& $out $GamePath $source $coreSource;if($LASTEXITCODE -ne 0){throw 'Tests failed.'}}

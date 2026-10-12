param([string]$Tests,[string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Star Renegades')
$ErrorActionPreference='Stop'
$managed=Join-Path $GamePath 'Star Renegades_Data/Managed'
$source=Join-Path $PSScriptRoot 'Source';$bin=Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$sdkLine=(& dotnet --list-sdks | Select-Object -Last 1)
if($sdkLine -notmatch '^([^ ]+) \[(.+)\]$'){throw 'Install a .NET SDK to build the mod.'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$manifestHash=(Get-FileHash -LiteralPath (Join-Path $source 'fixes.json')).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $source 'BuiltManifest.cs'),'namespace SRBugfix { internal static class BuiltManifest { internal const string Sha256="'+$manifestHash+'"; } }')
$options=@('/nologo','/nostdlib+','/langversion:7.3','/optimize+','/deterministic+')
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','Newtonsoft.Json.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
if($Tests -eq 'Core'){
 $out=Join-Path $bin 'CoreTests.exe';$options+=@('/target:exe',('/out:"'+$out+'"'));$files=@((Join-Path $source 'Overlay.cs'),(Join-Path $source 'Bootstrap.cs'),(Join-Path $source 'BuiltManifest.cs'),(Join-Path $PSScriptRoot 'Tests/CoreTests.cs'))
}else{throw 'Choose -Tests Core while the standalone loader is being built.'}
foreach($file in $files){$options+=('"'+$file+'"')}
$rsp=Join-Path $bin 'compile.rsp';[IO.File]::WriteAllLines($rsp,$options)
& dotnet $compiler ('@'+$rsp)
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
& $out $GamePath $source
if($LASTEXITCODE -ne 0){throw 'Tests failed.'}

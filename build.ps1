param([string]$Tests,[string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Star Renegades')
$ErrorActionPreference='Stop'
$managed=Join-Path $GamePath 'Star Renegades_Data/Managed'
$source=Join-Path $PSScriptRoot 'Source';$bin=Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$harmony=Join-Path $PSScriptRoot 'dependencies/0Harmony.dll'
$sdkLine=(& dotnet --list-sdks | Select-Object -Last 1)
if($sdkLine -notmatch '^([^ ]+) \[(.+)\]$'){throw 'Install a .NET SDK to build the mod.'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$manifestHash=(Get-FileHash -LiteralPath (Join-Path $source 'fixes.json')).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $source 'BuiltManifest.cs'),'namespace SRBugfix { internal static class BuiltManifest { internal const string Sha256="'+$manifestHash+'"; } }')
$options=@('/nologo','/nostdlib+','/langversion:7.3','/optimize+','/deterministic+')
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','Newtonsoft.Json.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
if(Test-Path -LiteralPath $harmony){$options+=('/reference:"'+$harmony+'"');Copy-Item -LiteralPath $harmony -Destination (Join-Path $bin '0Harmony.dll') -Force}
if($Tests -eq 'Core'){
 $out=Join-Path $bin 'CoreTests.exe';$options+=@('/target:exe',('/out:"'+$out+'"'));$files=@((Join-Path $source 'Overlay.cs'),(Join-Path $source 'Bootstrap.cs'),(Join-Path $source 'BuiltManifest.cs'),(Join-Path $PSScriptRoot 'Tests/CoreTests.cs'))
}elseif($Tests -eq 'Preview'){
 $out=Join-Path $bin 'PreviewTests.exe';$options+=@('/target:exe','/main:PreviewTests',('/out:"'+$out+'"'));$files=@('Source/Overlay.cs','Source/Bootstrap.cs','Source/BuiltManifest.cs','Source/PreviewFix.cs','Source/ReflectionPreviewFix.cs','Source/DamageInspectionFix.cs','Source/Activation.cs','Source/RuntimeProbe.cs','Source/Entrypoint.cs','Tests/CoreTests.cs','Tests/PreviewTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
}elseif(-not $Tests){
 $out=Join-Path $bin 'SRBugfix.dll';$options+=@('/target:library',('/out:"'+$out+'"'));$files=@(Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object FullName)
}elseif($Tests -eq 'Prediction'){
 $out=Join-Path $bin 'PredictionTests.exe';$options+=@('/target:exe','/main:PredictionTests',('/out:"'+$out+'"'));$files=@('Source/ReflectionPreviewFix.cs','Source/DamageInspectionFix.cs','Tests/PredictionTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
}else{throw 'Choose -Tests Core, Preview or Prediction.'}
foreach($file in $files){$options+=('"'+$file+'"')}
$rsp=Join-Path $bin 'compile.rsp';[IO.File]::WriteAllLines($rsp,$options)
& dotnet $compiler ('@'+$rsp)
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
if($Tests){& $out $GamePath $source;if($LASTEXITCODE -ne 0){throw 'Tests failed.'}}

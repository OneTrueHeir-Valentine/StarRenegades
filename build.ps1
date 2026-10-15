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
$options=@(('/pathmap:"'+$PSScriptRoot+'=/src"'),'/nologo','/nostdlib+','/langversion:7.3','/optimize+','/deterministic+')
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','Newtonsoft.Json.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
if(Test-Path -LiteralPath $harmony){$options+=('/reference:"'+$harmony+'"');Copy-Item -LiteralPath $harmony -Destination (Join-Path $bin '0Harmony.dll') -Force}
if(-not $Tests){
 $out=Join-Path $bin 'SRBugfix.dll';$options+=@('/target:library',('/out:"'+$out+'"'));$files=@(Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object FullName)
}else{
 $mains=@{Core='CoreTests';Preview='PreviewTests';Prediction='PredictionTests';Forecast='ForecastFollowupTests';Achievements='AchievementTests';Activation='ActivationTests';EnemyAI='EnemyAiTests';EnemyData='EnemyDataTests';ComboData='ComboDataTests';ComboEffects='ComboEffectTests';Resources='ResourceForecastTests';Effects='EffectLifecycleTests';EnemyConditions='EnemyConditionTests';Loading='LoadingTests'}
 if(-not $mains.ContainsKey($Tests)){throw 'Choose -Tests Core, Preview, Prediction, Forecast, Achievements, Activation, EnemyAI, EnemyData, ComboData, ComboEffects, Resources, Effects or EnemyConditions.'}
 $main=$mains[$Tests];$out=Join-Path $bin ($main+'.exe');$options+=@('/target:exe',('/main:'+$main),('/out:"'+$out+'"'))
 if($Tests -eq 'Forecast'){$options+='/define:FOLLOWUP_FIX'}
 if($Tests -eq 'Resources'){$options+='/define:RESOURCE_FIX'}
 $files=@(Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object FullName)
 $files+=Join-Path $PSScriptRoot ('Tests/'+$main+'.cs')
 if($Tests -eq 'Loading'){
  $files+=Join-Path $PSScriptRoot 'Tests/LoadingNativeTests.cs';$files+=Join-Path $PSScriptRoot 'Tests/LoadingDiagnosticTests.cs'
  $diagnosticRoot=Join-Path $bin 'diagnostic';New-Item -ItemType Directory -Force -Path $diagnosticRoot | Out-Null
  $diagnosticOptions=@($options | Where-Object {$_ -notlike '/target:*' -and $_ -notlike '/main:*' -and $_ -notlike '/out:*'})
  $diagnosticOptions+=@('/target:library',('/out:"'+(Join-Path $diagnosticRoot 'LoadingDiagnostic.dll')+'"'),('"'+(Join-Path $PSScriptRoot 'Tests/LoadingDiagnostic.cs')+'"'))
  $diagnosticResponse=Join-Path $diagnosticRoot 'compile.rsp';[IO.File]::WriteAllLines($diagnosticResponse,$diagnosticOptions)
  & dotnet $compiler ('@'+$diagnosticResponse);if($LASTEXITCODE -ne 0){throw 'Diagnostic compilation failed.'}
 }
 if($Tests -eq 'Preview' -or $Tests -eq 'Activation'){$files+=Join-Path $PSScriptRoot 'Tests/CoreTests.cs'}
}
foreach($file in $files){$options+=('"'+$file+'"')}
$rsp=Join-Path $bin 'compile.rsp';[IO.File]::WriteAllLines($rsp,$options)
& dotnet $compiler ('@'+$rsp)
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
if($Tests){& $out $GamePath $source;if($LASTEXITCODE -ne 0){throw 'Tests failed.'}}

param([Alias('GamePath')][string]$GameRoot='C:/Program Files (x86)/Steam/steamapps/common/Star Renegades',[switch]$BaselineData)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$managed=Join-Path $GameRoot 'Star Renegades_Data/Managed'
$outDir=Join-Path $project 'bin/combo-effects'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$sdk=(& dotnet --list-sdks|Select-Object -Last 1)
if($sdk -notmatch '^([^ ]+) \[(.+)\]$'){throw 'SDK missing'}
$csc=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$out=Join-Path $outDir 'ComboEffectTests.exe'
$options=@('/nologo','/nostdlib+','/langversion:7.3','/target:exe',('/out:"'+$out+'"'))
if($BaselineData){$options+='/define:BASELINE_COMBO_DATA'}
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
$harmony=Join-Path $project 'dependencies/0Harmony.dll';Copy-Item -LiteralPath $harmony -Destination $outDir -Force
$options+=('/reference:"'+$harmony+'"');$options+='"'+(Join-Path $PSScriptRoot 'ComboEffectTests.cs')+'"'
$source=Join-Path $project 'Source/ComboEffectFix.cs';if(Test-Path $source){$options+='"'+$source+'"'}
foreach($name in @('ProjectedEffectFix.cs','EffectLifecycleFix.cs','ReflectionPreviewFix.cs')){$options+='"'+(Join-Path $project ('Source/'+$name))+'"'}
$rsp=Join-Path $outDir 'combo-effects.rsp';[IO.File]::WriteAllLines($rsp,$options)
& dotnet $csc ('@'+$rsp);if($LASTEXITCODE){throw 'Combo effect compilation failed'}
& $out $GameRoot;exit $LASTEXITCODE

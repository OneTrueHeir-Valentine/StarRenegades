param([string]$GamePath='C:/Program Files (x86)/Steam/steamapps/common/Star Renegades',[switch]$Baseline)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$managed=Join-Path $GamePath 'Star Renegades_Data/Managed'
$outDir=Join-Path $root 'bin/forecast-tests'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$sdk=(& dotnet --list-sdks|Select-Object -Last 1)
if($sdk -notmatch '^([^ ]+) \[(.+)\]$'){throw 'SDK missing'}
$csc=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$out=Join-Path $outDir 'ForecastFollowupTests.exe'
$options=@('/nologo','/nostdlib+','/langversion:7.3','/target:exe',('/out:"'+$out+'"'))
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','Newtonsoft.Json.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
$harmony=Join-Path $root 'dependencies/0Harmony.dll'
$options+=('/reference:"'+$harmony+'"');Copy-Item -LiteralPath $harmony -Destination $outDir -Force
$files=@('Source/ReflectionPreviewFix.cs','Tests/ForecastFollowupTests.cs')
if(Test-Path (Join-Path $root 'Source/ProjectedEffectFix.cs')){$files+=@('Source/ProjectedEffectFix.cs','Source/EffectLifecycleFix.cs','Source/ComboEffectFix.cs');if(-not $Baseline){$options+='/define:FOLLOWUP_FIX'}}
foreach($file in $files){$options+='"'+(Join-Path $root $file)+'"'}
$rsp=Join-Path $outDir 'forecast-followup.rsp';[IO.File]::WriteAllLines($rsp,$options)
& dotnet $csc ('@'+$rsp);if($LASTEXITCODE){throw 'Forecast fixture compilation failed'}
& $out $GamePath;if($LASTEXITCODE){throw 'Forecast fixture failed'}

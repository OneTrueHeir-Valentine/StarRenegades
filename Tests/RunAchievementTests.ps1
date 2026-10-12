param([string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Star Renegades')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskBin=Join-Path $taskRoot 'bin/achievement-tests'
New-Item -ItemType Directory -Force -Path $taskBin | Out-Null
$managed=Join-Path $GamePath 'Star Renegades_Data/Managed'
$sdkLine=(& dotnet --list-sdks | Select-Object -Last 1)
if($sdkLine -notmatch '^([^ ]+) \[(.+)\]$'){throw 'Install a .NET SDK to build tests.'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$out=Join-Path $taskBin 'AchievementTests.exe'
$options=@('/nologo','/nostdlib+','/langversion:7.3','/optimize+','/target:exe','/main:AchievementTests',('/out:"'+$out+'"'))
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','Newtonsoft.Json.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
$harmony=Join-Path $taskRoot 'dependencies/0Harmony.dll'
$options+=('/reference:"'+$harmony+'"')
Copy-Item -LiteralPath $harmony -Destination (Join-Path $taskBin '0Harmony.dll') -Force
foreach($file in @('Source/AchievementFix.cs','Tests/AchievementTests.cs')){$options+=('"'+(Join-Path $taskRoot $file)+'"')}
$rsp=Join-Path $taskBin 'achievement-compile.rsp'
[IO.File]::WriteAllLines($rsp,$options)
& dotnet $compiler ('@'+$rsp)
if($LASTEXITCODE -ne 0){throw 'Achievement test compilation failed.'}
& $out $GamePath
if($LASTEXITCODE -ne 0){throw 'Achievement tests failed.'}

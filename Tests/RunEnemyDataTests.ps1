param([string]$GameRoot='C:/Program Files (x86)/Steam/steamapps/common/Star Renegades')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$managed=Join-Path $GameRoot 'Star Renegades_Data/Managed'
$output=Join-Path $project 'bin'
[IO.Directory]::CreateDirectory($output)|Out-Null
$sdkLine=(& dotnet --list-sdks | Select-Object -Last 1)
if($sdkLine -notmatch '^([^ ]+) \[(.+)\]$'){throw 'A .NET SDK is required.'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$executable=Join-Path $output 'EnemyDataTests.exe'
$options=@('/nologo','/nostdlib+','/langversion:7.3','/optimize+','/target:exe',('/out:"'+$executable+'"'))
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
$options+=('"'+(Join-Path $PSScriptRoot 'EnemyDataTests.cs')+'"')
$implementation=Join-Path $project 'Source/EnemyDataFix.cs'
if(Test-Path -LiteralPath $implementation){$options+=('"'+$implementation+'"')}
$response=Join-Path $output 'enemy-data.rsp'
[IO.File]::WriteAllLines($response,$options)
& dotnet $compiler ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
& $executable $GameRoot
if($LASTEXITCODE -ne 0){throw 'Enemy data tests failed.'}

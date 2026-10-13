param(
 [string]$GamePath='C:/Program Files (x86)/Steam/steamapps/common/Star Renegades',
 [switch]$NativeBaseline
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$managed=Join-Path $GamePath 'Star Renegades_Data/Managed'
if((Get-FileHash -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll')).Hash.ToLowerInvariant() -ne 'a9d49075bab3953b7748b7e91d94829d325580e870297df8ebdc8d43f55427d0'){throw 'Unsupported game assembly for native enemy AI tests.'}
$taskBin=Join-Path $taskRoot 'bin/enemy-ai-tests'
New-Item -ItemType Directory -Force -Path $taskBin | Out-Null
$sdkLine=(& dotnet --list-sdks | Select-Object -Last 1)
if($sdkLine -notmatch '^([^ ]+) \[(.+)\]$'){throw 'Install a .NET SDK to build tests.'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$out=Join-Path $taskBin 'EnemyAiTests.exe'
$options=@('/nologo','/nostdlib+','/langversion:7.3','/optimize+','/target:exe','/main:EnemyAiTests',('/out:"'+$out+'"'))
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll')){$options+=('/reference:"'+(Join-Path $managed $name)+'"')}
$harmony=Join-Path $taskRoot 'dependencies/0Harmony.dll'
$options+=('/reference:"'+$harmony+'"')
Copy-Item -LiteralPath $harmony -Destination (Join-Path $taskBin '0Harmony.dll') -Force
if($NativeBaseline){$options+='/define:NATIVE_BASELINE'}else{$options+=('"'+(Join-Path $taskRoot 'Source/EnemyAiFix.cs')+'"')}
$options+=('"'+(Join-Path $PSScriptRoot 'EnemyAiTests.cs')+'"')
$rsp=Join-Path $taskBin 'enemy-ai-compile.rsp'
[IO.File]::WriteAllLines($rsp,$options)
& dotnet $compiler ('@'+$rsp)
if($LASTEXITCODE -ne 0){throw 'Enemy AI test compilation failed.'}
& $out $GamePath
if($LASTEXITCODE -ne 0){throw 'Enemy AI tests failed.'}

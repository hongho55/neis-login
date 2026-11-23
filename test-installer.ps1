$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskTests=Join-Path $PSScriptRoot 'bin\installer-tests'
New-Item -ItemType Directory -Path $taskTests -Force | Out-Null
$taskChild=Join-Path $taskTests 'TestChild.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 ('/out:'+$taskChild) (Join-Path $PSScriptRoot 'installer\tests\TestChild.cs')
if($LASTEXITCODE -ne 0){throw '설치 시험 자식 프로세스 빌드 실패'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TeacherWorkStart.exe.config') -Destination ($taskChild+'.config') -Force
$taskRunner=Join-Path $taskTests 'SetupTests.exe'
$taskArguments=@('/nologo','/target:exe','/platform:x64','/langversion:5',('/out:'+$taskRunner),'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll')
foreach($taskFile in @('SetupCore.cs','SetupPlatform.cs','SetupWindow.cs','SetupTests.cs')){$taskArguments+=Join-Path $PSScriptRoot ('installer\'+$taskFile)}
& $taskCompiler @taskArguments
if($LASTEXITCODE -ne 0){throw '설치 회귀 검사 빌드 실패'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TeacherWorkStart.exe.config') -Destination ($taskRunner+'.config') -Force
& $taskRunner $taskChild
if($LASTEXITCODE -ne 0){throw '설치 회귀 검사 실패'}

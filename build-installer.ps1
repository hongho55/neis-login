$ErrorActionPreference = 'Stop'
$taskApp = Join-Path $PSScriptRoot 'TeacherWorkStart.exe'
if (-not (Test-Path -LiteralPath $taskApp)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$taskConfiguration = $taskApp + '.config'
if (-not (Test-Path -LiteralPath $taskConfiguration)) { throw '도우미 설정 파일이 없습니다.' }
$taskVersion = (Get-Item -LiteralPath $taskApp).VersionInfo.FileVersion
if ($taskVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw '도우미 버전을 확인하지 못했습니다.' }
$taskBuild = Join-Path $PSScriptRoot 'bin\installer'
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskManifest = Join-Path $taskBuild 'PayloadMetadata.cs'
$taskExeHash = (Get-FileHash -LiteralPath $taskApp -Algorithm SHA256).Hash
$taskConfigHash = (Get-FileHash -LiteralPath $taskConfiguration -Algorithm SHA256).Hash
$taskSource = 'namespace TeacherWorkStartSetup { internal static class PayloadMetadata { internal const string Version="' + $taskVersion + '"; internal const string ExecutableHash="' + $taskExeHash + '"; internal const string ConfigurationHash="' + $taskConfigHash + '"; } }'
[IO.File]::WriteAllText($taskManifest, $taskSource, (New-Object Text.UTF8Encoding($false)))
$taskOutput = Join-Path $taskBuild ('TeacherWorkStart-Setup-' + ([Version]$taskVersion).ToString(3) + '.exe')
$taskArguments = @('/nologo','/target:winexe','/platform:x64','/langversion:5','/optimize+',('/out:'+$taskOutput),('/win32manifest:'+(Join-Path $PSScriptRoot 'installer\setup.manifest')),'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',('/resource:'+$taskApp+',TeacherWorkStartSetup.Application'),('/resource:'+$taskConfiguration+',TeacherWorkStartSetup.Configuration'),$taskManifest)
$taskArguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'installer') -Filter '*.cs' | Where-Object Name -ne 'SetupTests.cs' | ForEach-Object FullName)
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') @taskArguments
if ($LASTEXITCODE -ne 0) { throw '설치 프로그램 빌드 실패' }
[pscustomobject]@{Installer=$taskOutput;ApplicationVersion=$taskVersion;SHA256=(Get-FileHash -LiteralPath $taskOutput).Hash;PayloadSHA256=$taskExeHash} | ConvertTo-Json

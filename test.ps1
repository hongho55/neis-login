$ErrorActionPreference = 'Stop'
# Builds tests\RegressionTests.csproj (src + tests) and runs the harness. No real credentials,
# browser login, user settings or Startup entries are touched.
$msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild)) { throw 'MSBuild(.NET Framework 4.x)를 찾지 못했습니다.' }
$project = Join-Path $PSScriptRoot 'tests\RegressionTests.csproj'
& $msbuild $project /nologo /v:minimal /t:Rebuild /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw '회귀 테스트 빌드 실패' }
& (Join-Path $PSScriptRoot 'bin\tests\RegressionTests.exe')
if ($LASTEXITCODE -ne 0) { throw '회귀 테스트 실패' }

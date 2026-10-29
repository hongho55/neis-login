$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $PSScriptRoot 'bin\tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$arguments = @('/nologo','/target:exe','/platform:x64','/langversion:5','/define:CONSOLE','/main:RegressionTests',('/out:' + (Join-Path $testOutput 'RegressionTests.exe')))
foreach ($library in @('UIAutomationClient','UIAutomationTypes','WindowsBase','PresentationCore','PresentationFramework','WindowsFormsIntegration')) { $arguments += '/reference:' + (Join-Path $framework ('WPF\' + $library + '.dll')) }
$arguments += '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Xaml.dll'
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$arguments += Join-Path $PSScriptRoot 'tests\RegressionTests.cs'
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw '회귀 테스트 빌드 실패' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TeacherWorkStart.exe.config') -Destination (Join-Path $testOutput 'RegressionTests.exe.config') -Force
& (Join-Path $testOutput 'RegressionTests.exe')
if ($LASTEXITCODE -ne 0) { throw '회귀 테스트 실패' }

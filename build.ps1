$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', ('/out:' + (Join-Path $PSScriptRoot 'TeacherWorkStart.exe')))
foreach ($library in @('UIAutomationClient', 'UIAutomationTypes', 'WindowsBase', 'PresentationCore', 'PresentationFramework', 'WindowsFormsIntegration')) {
  $arguments += '/reference:' + (Join-Path $framework ('WPF\' + $library + '.dll'))
}
$arguments += '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Xaml.dll'
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }
$consoleArguments = @('/target:exe', '/define:CONSOLE', ('/out:' + (Join-Path $PSScriptRoot 'TeacherWorkStart.Cli.exe'))) + @($arguments | Where-Object { $_ -notlike '/target:*' -and $_ -notlike '/out:*' })
& $compiler @consoleArguments
if ($LASTEXITCODE -ne 0) { throw '진단 도구 빌드 실패' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TeacherWorkStart.exe.config') -Destination (Join-Path $PSScriptRoot 'TeacherWorkStart.Cli.exe.config') -Force

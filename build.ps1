$ErrorActionPreference = 'Stop'
# Builds the GUI helper and the console diagnostic build through TeacherWorkStart.csproj, then
# copies them next to this script, where build-installer.ps1 expects TeacherWorkStart.exe.
$msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild)) { throw 'MSBuild(.NET Framework 4.x)를 찾지 못했습니다.' }
$project = Join-Path $PSScriptRoot 'TeacherWorkStart.csproj'
foreach ($configuration in @('Release', 'Cli')) {
  & $msbuild $project /nologo /v:minimal /t:Rebuild ('/p:Configuration=' + $configuration)
  if ($LASTEXITCODE -ne 0) { throw ("빌드 실패: " + $configuration) }
}
$artifacts = @{
  'bin\Release\TeacherWorkStart.exe'                = 'TeacherWorkStart.exe'
  'bin\Release\TeacherWorkStart.exe.config'         = 'TeacherWorkStart.exe.config'
  'bin\Cli\TeacherWorkStart.Cli.exe'                = 'TeacherWorkStart.Cli.exe'
  'bin\Cli\TeacherWorkStart.Cli.exe.config'         = 'TeacherWorkStart.Cli.exe.config'
}
foreach ($source in $artifacts.Keys) {
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot $source) -Destination (Join-Path $PSScriptRoot $artifacts[$source]) -Force
}

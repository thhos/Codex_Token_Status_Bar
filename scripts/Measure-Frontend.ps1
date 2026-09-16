param([string]$BinaryDirectory = 'build-next')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $projectRoot $BinaryDirectory
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') /nologo /platform:x64 /codepage:65001 "/win32manifest:$projectRoot\src\windows\app.manifest" "/reference:$target\CodexPetCredits.exe" "/out:$target\FrontendPerformance.exe" @references (Join-Path $projectRoot 'tests\FrontendPerformance.cs')
if ($LASTEXITCODE -ne 0) { throw 'Performance harness build failed.' }
Copy-Item -LiteralPath (Join-Path $target 'CodexPetCredits.exe.config') -Destination (Join-Path $target 'FrontendPerformance.exe.config') -Force
& (Join-Path $target 'FrontendPerformance.exe') $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Performance harness failed.' }

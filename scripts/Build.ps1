$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler is required.' }
$nodeCommand = Get-Command node -ErrorAction Stop
$nodeMajor = [int]((& $nodeCommand.Source --version).TrimStart('v').Split('.')[0])
if ($nodeMajor -lt 24) { throw 'Node.js 24 or newer is required; no packages will be installed.' }
$references = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'WPF\UIAutomationProvider.dll', 'WPF\UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) }
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\windows') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$outputDir\CodexPetCredits.exe" "/win32manifest:$projectRoot\src\windows\app.manifest" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'WPF build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\windows\App.config') -Destination (Join-Path $outputDir 'CodexPetCredits.exe.config') -Force
Write-Output 'Built build\CodexPetCredits.exe (no external packages).'

param([string]$BinaryDirectory = 'build-next')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $projectRoot $BinaryDirectory
$geometryPath = Join-Path $projectRoot 'artifacts\probe-pet.json'
# Metadata only: no prompt text, account credentials or user input injection.
@'
import {CodexMetadata} from './src/backend/cdp.mjs';
const c=new CodexMetadata(9337);
try { await c.poll(); await c.pollPet(); if(!c.pet?.mascot) throw Error('Pet unavailable'); console.log(JSON.stringify({pet:c.pet.mascot,visible:c.pet.visible,observedAt:c.petObservedAt})); }
finally { c.close(); }
'@ | node --input-type=module | Set-Content -LiteralPath $geometryPath -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'Pet probe unavailable' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') /nologo /platform:x64 /codepage:65001 "/reference:$target\CodexPetCredits.exe" "/out:$target\FrontendProbe.exe" @references (Join-Path $projectRoot 'tests\FrontendProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
& (Join-Path $target 'FrontendProbe.exe') $projectRoot $geometryPath
if ($LASTEXITCODE -ne 0) { throw 'Live popup probe failed' }

param([string]$BinaryDirectory = 'build-next')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'package.json') -Raw | ConvertFrom-Json).version
$releaseRoot = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$archive = Join-Path $releaseRoot "Codex_Token_Status_Bar-v$version-windows-x64.zip"
$binary = Join-Path $projectRoot "$BinaryDirectory\CodexPetCredits.exe"
if (-not (Test-Path -LiteralPath $binary)) { throw 'Build the release executable first.' }

# An explicit manifest avoids packaging local history, prev, logs, credentials or runtime caches.
$files = @('README.md','package.json','Start.cmd')
foreach ($folder in @('assets','src','scripts','tests')) {
    $files += Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -Recurse -File | ForEach-Object { $_.FullName.Substring($projectRoot.Length + 1) }
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($archive,[IO.FileMode]::Create)
$zip = New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $projectRoot $file),$file.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    foreach ($name in @('CodexPetCredits.exe','CodexPetCredits.exe.config')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $projectRoot "$BinaryDirectory\$name"),"build/$name",[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Select-Object Path,Hash

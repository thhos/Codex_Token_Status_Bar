param([switch]$CompanionOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'build\CodexPetCredits.exe'
if (-not (Test-Path -LiteralPath $executable)) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$env:CODEX_STATUS_NODE = (Get-Command node -ErrorAction Stop).Source
if (-not $CompanionOnly) {
    $package = Get-AppxPackage -Name 'OpenAI.Codex' | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $package) { throw 'Codex desktop installation not found.' }
    $codexDesktop = Join-Path $package.InstallLocation 'app\ChatGPT.exe'
    # A running Electron instance cannot acquire new debug flags. Never kill an active conversation.
    $existing = Get-Process ChatGPT -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $codexDesktop }
    if (-not $existing) {
        $occupied = Get-NetTCPConnection -LocalPort 9337 -State Listen -ErrorAction SilentlyContinue
        if ($occupied) { throw 'Local port 9337 is occupied. Close the conflicting listener before starting Codex.' }
        Start-Process -FilePath $codexDesktop -ArgumentList @('--remote-debugging-address=127.0.0.1', '--remote-debugging-port=9337') -WindowStyle Hidden
    } else {
        try { $null = Invoke-RestMethod -Uri 'http://127.0.0.1:9337/json/list' -TimeoutSec 2 }
        catch {
            Add-Type -AssemblyName PresentationFramework
            [System.Windows.MessageBox]::Show('Please fully quit Codex, then open Codex + Credits again to enable the pet status bar. Your current conversations have not been interrupted.', 'Codex + Credits') | Out-Null
            return
        }
    }
}
Start-Process -FilePath $executable -WorkingDirectory $projectRoot -WindowStyle Hidden

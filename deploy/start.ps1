<#
.SYNOPSIS
  Starts Waypoint in Podman (Windows / WSL2) and opens it in the browser.
.PARAMETER Build
  Rebuild the api and web images first (after pulling new code).
#>
param([switch]$Build)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command podman -ErrorAction SilentlyContinue)) {
    throw 'podman was not found. Install Podman Desktop (https://podman-desktop.io) and reopen the terminal.'
}

# Make sure the Podman machine (WSL2 VM) is running.
$running = podman machine list --format '{{.Running}}' 2>$null | Select-String -SimpleMatch 'true'
if (-not $running) {
    Write-Host 'Starting the Podman machine...'
    podman machine start
}

$composeArgs = @('compose', '-f', 'compose.yaml', 'up', '-d')
if ($Build) { $composeArgs += '--build' }
podman @composeArgs
if ($LASTEXITCODE -ne 0) { throw 'podman compose failed.' }

$port = if ($env:WAYPOINT_WEB_PORT) { $env:WAYPOINT_WEB_PORT } else { '8080' }
$url = "http://127.0.0.1:$port/"
Write-Host "Waiting for Waypoint at $url ..."
for ($i = 0; $i -lt 60; $i++) {
    try {
        $r = Invoke-WebRequest -Uri "${url}api/health" -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { break }
    } catch { Start-Sleep -Seconds 2 }
}
Write-Host "Waypoint is up: $url"
Start-Process $url

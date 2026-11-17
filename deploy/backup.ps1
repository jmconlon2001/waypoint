<#
.SYNOPSIS
  Dumps the Waypoint database to deploy/backups/waypoint-yyyyMMdd-HHmmss.dump (pg_dump custom format).
.EXAMPLE
  ./backup.ps1
  # Restore into the running container:
  # podman exec -i waypoint-db pg_restore -U waypoint -d waypoint --clean --if-exists < backups/waypoint-20261117-180000.dump
#>
param([int]$KeepDays = 30)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$user = if ($env:WAYPOINT_DB_USER) { $env:WAYPOINT_DB_USER } else { 'waypoint' }
$db = if ($env:WAYPOINT_DB_NAME) { $env:WAYPOINT_DB_NAME } else { 'waypoint' }
New-Item -ItemType Directory -Force -Path backups | Out-Null
$file = Join-Path 'backups' ("waypoint-{0}.dump" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

# pg_dump runs inside the container and writes to stdout; dump to a file inside the container and
# copy it out so PowerShell doesn't re-encode the binary stream.
podman exec waypoint-db pg_dump -U $user -d $db -Fc -f /tmp/waypoint.dump
if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed. Is the waypoint-db container running?' }
podman cp waypoint-db:/tmp/waypoint.dump $file
podman exec waypoint-db rm -f /tmp/waypoint.dump | Out-Null
Write-Host "Backup written: $file"

if ($KeepDays -gt 0) {
    Get-ChildItem backups -Filter 'waypoint-*.dump' |
        Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) } |
        Remove-Item -Verbose
}

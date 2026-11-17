#!/usr/bin/env sh
# Dumps the Waypoint database to deploy/backups/waypoint-YYYYmmdd-HHMMSS.dump (pg_dump custom format).
# Restore: podman exec -i waypoint-db pg_restore -U waypoint -d waypoint --clean --if-exists < backups/<file>.dump
set -eu
cd "$(dirname "$0")"
if command -v podman >/dev/null 2>&1; then ENGINE=podman; else ENGINE=docker; fi

USER_NAME="${WAYPOINT_DB_USER:-waypoint}"
DB_NAME="${WAYPOINT_DB_NAME:-waypoint}"
KEEP_DAYS="${KEEP_DAYS:-30}"
mkdir -p backups
FILE="backups/waypoint-$(date +%Y%m%d-%H%M%S).dump"

"$ENGINE" exec waypoint-db pg_dump -U "$USER_NAME" -d "$DB_NAME" -Fc > "$FILE"
echo "Backup written: $FILE"

if [ "$KEEP_DAYS" -gt 0 ]; then
  find backups -name 'waypoint-*.dump' -mtime +"$KEEP_DAYS" -print -delete
fi

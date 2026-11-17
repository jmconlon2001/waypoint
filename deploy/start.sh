#!/usr/bin/env sh
# Starts Waypoint with Podman (or Docker) compose. Pass --build to rebuild the images.
set -eu
cd "$(dirname "$0")"

if command -v podman >/dev/null 2>&1; then ENGINE=podman; else ENGINE=docker; fi
if [ "${1:-}" = "--build" ]; then "$ENGINE" compose -f compose.yaml up -d --build; else "$ENGINE" compose -f compose.yaml up -d; fi

PORT="${WAYPOINT_WEB_PORT:-8080}"
echo "Waiting for Waypoint at http://127.0.0.1:$PORT/ ..."
i=0
while [ $i -lt 60 ]; do
  if curl -fsS "http://127.0.0.1:$PORT/api/health" >/dev/null 2>&1; then echo "Waypoint is up: http://127.0.0.1:$PORT/"; exit 0; fi
  i=$((i + 1)); sleep 2
done
echo "Waypoint did not answer yet; check: $ENGINE compose -f compose.yaml logs api" >&2
exit 1

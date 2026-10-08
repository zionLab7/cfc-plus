#!/bin/bash
# A restored volume includes all databases, attachments and encryption keys.
set -euo pipefail
school="${1:?backup label required}"
archive="${2:?absolute backup file required}"
[[ "$school" =~ ^[a-z][a-z0-9-]{2,47}$ ]] || { echo 'Invalid school identifier'; exit 1; }
[[ "$archive" == /* && -f "$archive" ]] || { echo 'Use an existing absolute backup file'; exit 1; }
volume="${3:-cfc-${school}-restore-$(date -u +%Y%m%dT%H%M%SZ)}"
[[ "$volume" =~ ^[a-z0-9][a-z0-9_.-]+$ ]] || { echo 'Invalid volume name'; exit 1; }
if docker volume inspect "$volume" >/dev/null 2>&1; then echo 'Target volume already exists. Restore to a NEW central volume and verify before switching.'; exit 1; fi
docker volume create "$volume" >/dev/null
docker run --rm --network none --user 0:0 --mount "type=volume,source=$volume,target=/data" --mount "type=bind,source=$archive,target=/backup.tar.gz,readonly" --entrypoint tar ubuntu:24.04 -xzf /backup.tar.gz -C /data
echo "Volume restored: $volume. Set CFC_DATA_VOLUME to this name; the central registry and all school IDs are preserved. Verify before admitting users."

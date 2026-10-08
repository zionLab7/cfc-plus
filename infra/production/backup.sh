#!/bin/bash
# Consistent, private full-volume backup. Run on the VPS as its operator.
set -euo pipefail
school="${1:?backup label required}"
output="${2:?absolute backup directory required}"
[[ "$school" =~ ^[a-z][a-z0-9-]{2,47}$ ]] || { echo 'Invalid school identifier'; exit 1; }
[[ "$output" == /* ]] || { echo 'Use an absolute backup directory'; exit 1; }
volume="${3:-cfc-central-data}"
[[ "$volume" =~ ^[a-z0-9][a-z0-9_.-]+$ ]] || { echo 'Invalid volume name'; exit 1; }
docker volume inspect "$volume" >/dev/null
containers=$(docker ps -q --filter "volume=$volume")
[[ -n "$containers" ]] || { echo 'No running central container found'; exit 1; }
umask 077
mkdir -p "$output"
file="cfc-${school}-$(date -u +%Y%m%dT%H%M%SZ).tar.gz"
# Resume exactly the containers we stopped, even if the backup command fails.
trap 'docker start $containers >/dev/null' EXIT
docker stop -t 45 $containers >/dev/null
docker run --rm --network none --user 0:0 --mount "type=volume,source=$volume,target=/data,readonly" --mount "type=bind,source=$output,target=/backup" --entrypoint tar ubuntu:24.04 -czf "/backup/$file" -C /data .
chmod 600 "$output/$file"
echo "Backup saved: $output/$file"
echo 'Contains personal data and keys: encrypt and store outside this VPS.'

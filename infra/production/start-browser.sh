#!/bin/sh
set -eu
umask 077
mkdir -p /data/home
exec xvfb-run -a --server-args="-screen 0 1280x800x24 -nolisten tcp" dotnet /app/CfcPilot.dll --urls http://0.0.0.0:8080

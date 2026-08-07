#!/bin/bash
set -euo pipefail
cd /opt/smms/app
docker compose build --progress plain smms-api 2>&1 | tail -n 25
echo "--- images ---"
docker images --format '{{.Repository}}:{{.Tag}} {{.Size}}' | head -n 10

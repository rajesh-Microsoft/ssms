#!/bin/bash
# Read-only: reports what promote.ps1 needs to target the pre-prod stack exactly.
cd /opt/smms/app
echo "--- compose project ---"
grep -m1 '^name:' docker-compose.yml || echo "(none)"
echo "--- api image ---"
docker compose images smms-api 2>/dev/null || true
echo "--- images ---"
docker images --format '{{.Repository}}:{{.Tag}}' | grep -i smms || true
echo "--- state files ---"
for f in DEPLOYED_COMMIT DEPLOYED_VERIFIED .env; do
  [ -f "$f" ] && echo "$f present" || echo "$f MISSING"
done
echo "--- containers ---"
docker ps --format '{{.Names}} {{.Status}}'
echo "__PROMOTE_OK__"

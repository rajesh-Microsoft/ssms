#!/bin/bash
set -uo pipefail
cd /opt/smms/app
docker compose up -d smms-api
sleep 25
echo "--- containers ---"
docker ps --format '{{.Names}}\t{{.Status}}'
echo "--- api log (tenant readiness) ---"
docker logs smms-pprod-api 2>&1 | grep -iE 'Database is ready|Now listening|Application started|error|fail' | tail -n 25
echo "--- internal API probe (Host: aadya.yuvaansoft.shop) ---"
docker run --rm --network smms-pprod_default curlimages/curl:latest \
  -s -o /dev/null -w 'GET /api/health -> %{http_code}\n' -H 'Host: aadya.yuvaansoft.shop' http://smms-api:8080/api/health || true
docker run --rm --network smms-pprod_default curlimages/curl:latest \
  -s -w '\nlogin -> %{http_code}\n' -H 'Host: aadya.yuvaansoft.shop' -H 'Content-Type: application/json' \
  -d '{"username":"nope","password":"nope"}' http://smms-api:8080/api/auth/login | tail -n 2 || true

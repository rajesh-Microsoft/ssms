# Stage 3: build and start the dev API + UI.
$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
set -e
cd ~/smms-dev/SMMS
docker compose up -d --build
echo "===== WAIT FOR API ====="
for i in $(seq 1 90); do
  code=$(docker exec smms-dev-ui wget -qO- --server-response \
          --header="Host: demo.yuvaansoft.shop" http://localhost/api/health 2>&1 \
          | awk '/HTTP\//{print $2}' | tail -1)
  if [ -n "$code" ]; then echo "api reachable after ${i}s (status $code)"; break; fi
  sleep 1
done
echo "===== CONTAINERS ====="
docker ps -a --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
echo "===== DEV API STARTUP LOG (tail) ====="
docker logs smms-dev-api 2>&1 | tail -30
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

# Stage 5: the ONLY production change - add the dev routing block to nginx.conf.
# Backs up first, validates with `nginx -t` BEFORE reloading, and restores the
# backup automatically if validation fails. A reload is graceful: existing
# connections are served by the old workers, so there is no dropped request.
$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

scp -i $key nginx\nginx.conf "${vm}:~/nginx.conf.new" | Out-Null

$remote = @'
set -e
CONF=~/smms/SMMS/nginx/nginx.conf
STAMP=$(date +%Y%m%dT%H%M%SZ)
BAK=~/nginx.conf.bak-$STAMP

cp "$CONF" "$BAK"
echo "backup: $BAK"

cp ~/nginx.conf.new "$CONF"
rm -f ~/nginx.conf.new

if docker exec smms-ui nginx -t 2>&1 | sed 's/^/  nginx -t: /'; then
  docker exec smms-ui nginx -s reload
  echo "RESULT: config valid, nginx reloaded"
else
  cp "$BAK" "$CONF"
  echo "RESULT: VALIDATION FAILED - original config restored, nothing reloaded"
  exit 1
fi

echo "===== PRODUCTION STILL SERVING ====="
for h in ssms.yuvaansoft.shop admin.yuvaansoft.shop www.yuvaansoft.shop; do
  printf "  %-28s %s\n" "$h" "$(curl -sk -o /dev/null -w '%{http_code}' https://127.0.0.1/ -H "Host: $h")"
done
echo "===== DEV NOW ROUTED ====="
for h in dev-demo.yuvaansoft.shop dev-admin.yuvaansoft.shop; do
  printf "  %-28s %s\n" "$h" "$(curl -sk -o /dev/null -w '%{http_code}' https://127.0.0.1/ -H "Host: $h")"
done
echo "===== CONTAINER UPTIME (prod must not have restarted) ====="
docker ps --format 'table {{.Names}}\t{{.Status}}'
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

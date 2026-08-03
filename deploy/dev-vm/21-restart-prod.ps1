$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS
docker compose up -d smms-api > /tmp/up.log 2>&1
echo "UP EXIT CODE: $?"
sleep 25
echo "----- container state -----"
docker ps --filter name=smms-api --format 'table {{.Names}}\t{{.Status}}'
echo "----- tenant startup results -----"
docker logs smms-api 2>&1 | grep -E "Database is ready|Giving up|Connection failed" | tail -n 20
echo "----- public health check -----"
curl -s -o /dev/null -w 'prod portal   : %{http_code}\n' https://ssms.yuvaansoft.shop/ -k
curl -s -o /dev/null -w 'prod admin    : %{http_code}\n' https://admin.yuvaansoft.shop/ -k
curl -s -o /dev/null -w 'prod marketing: %{http_code}\n' https://www.yuvaansoft.shop/ -k
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

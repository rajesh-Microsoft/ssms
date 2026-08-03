$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "----- public endpoints -----"
curl -s -o /dev/null -w 'prod marketing: %{http_code}\n' https://www.yuvaansoft.shop/
curl -s -o /dev/null -w 'prod portal   : %{http_code}\n' https://ssms.yuvaansoft.shop/
curl -s -o /dev/null -w 'dev  demo     : %{http_code}\n' https://dev-demo.yuvaansoft.shop/
echo "----- prod razorpay still dormant (expect 404) -----"
curl -s -o /dev/null -w 'prod webhook  : %{http_code}\n' -X POST https://ssms.yuvaansoft.shop/api/webhooks/razorpay
echo "----- any unhandled exceptions since restart -----"
docker logs smms-api 2>&1 | grep -icE "unhandled|fatal|stack trace"
echo "----- patched code present in running image -----"
docker exec smms-api sh -c 'ls -1 /app/SMMS.Api.dll' 2>&1
docker ps --filter name=smms --format 'table {{.Names}}\t{{.Status}}'
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

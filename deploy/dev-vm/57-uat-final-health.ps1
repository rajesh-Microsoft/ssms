$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS
echo "===== DEPLOYED COMMIT ====="
cat DEPLOYED_COMMIT

echo ""
echo "===== TENANTS READY ====="
docker logs smms-api 2>&1 | grep -c "Database is ready" ; echo "(expect 7)"

echo ""
echo "===== EXCEPTIONS ====="
docker logs smms-api 2>&1 | grep -iE "unhandled|exception|fail: " | head -n 10 ; echo "(nothing above = clean)"

echo ""
echo "===== PUBLIC SITES ====="
curl -s -o /dev/null -w "aadya : %{http_code}\n" https://aadya.yuvaansoft.shop/
curl -s -o /dev/null -w "demo  : %{http_code}\n" https://demo.yuvaansoft.shop/
curl -s -o /dev/null -w "webhook via nginx : %{http_code}\n" -X POST https://demo.yuvaansoft.shop/api/webhooks/razorpay -H "Content-Type: application/json" -d '{}'

echo ""
echo "===== CONTAINERS ====="
docker ps --format '{{.Names}}\t{{.Status}}' ; echo "--- end ---"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

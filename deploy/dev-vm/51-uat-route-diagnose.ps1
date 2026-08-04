$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== RAW LOGIN VIA PUBLIC HOST ====="
curl -s -i -X POST https://demo.yuvaansoft.shop/api/auth/login -H "Content-Type: application/json" -d '{"username":"res-a1004","password":"demo123"}' | head -n 20
echo "--- end ---"

echo ""
echo "===== RAW LOGIN DIRECT TO CONTAINER (Host header set) ====="
curl -s -i -X POST http://localhost:7253/api/auth/login -H "Host: demo.yuvaansoft.shop" -H "Content-Type: application/json" -d '{"username":"res-a1004","password":"demo123"}' | head -n 20
echo "--- end ---"

echo ""
echo "===== WEBHOOK DIRECT TO CONTAINER ====="
curl -s -o /dev/null -w "direct webhook: %{http_code}\n" -X POST http://localhost:7253/api/webhooks/razorpay -H "Host: demo.yuvaansoft.shop" -H "Content-Type: application/json" -d '{}'

echo ""
echo "===== WEBHOOK THROUGH NGINX ====="
curl -s -i -X POST https://demo.yuvaansoft.shop/api/webhooks/razorpay -H "Content-Type: application/json" -d '{}' | head -n 12
echo "--- end ---"

echo ""
echo "===== IS THE RAZORPAY CONTROLLER IN THE RUNNING IMAGE? ====="
docker exec smms-api ls -la /app/SMMS.Api.dll ; echo "--- dll above ---"
docker logs smms-api 2>&1 | grep -iE "razorpay" | head -n 10 ; echo "(any razorpay log lines above)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

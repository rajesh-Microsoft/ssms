$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS
echo "===== TENANTS READY ====="
docker logs smms-api 2>&1 | grep -c "Database is ready" ; echo "--- count above ---"
docker logs smms-api 2>&1 | grep "Database is ready" ; echo "--- end list ---"

echo ""
echo "===== ERRORS / EXCEPTIONS ====="
docker logs smms-api 2>&1 | grep -iE "unhandled|exception|fail: " | head -n 15 ; echo "(nothing above = clean)"

echo ""
echo "===== PUBLIC HEALTH (through nginx) ====="
curl -s -o /dev/null -w "aadya home  : %{http_code}\n" https://aadya.yuvaansoft.shop/
curl -s -o /dev/null -w "api swagger : %{http_code}\n" https://aadya.yuvaansoft.shop/api/

echo ""
echo "===== MEMBER LOGIN + PAYMENT OPTIONS (demo society) ====="
TOKEN=$(curl -s -X POST https://demo.yuvaansoft.shop/api/auth/login -H "Content-Type: application/json" -d '{"username":"res-a1004","password":"demo123"}' | python3 -c "import sys,json;print(json.load(sys.stdin).get('token',''))")
echo "token length: ${#TOKEN}"
curl -s https://demo.yuvaansoft.shop/api/member/payment-options -H "Authorization: Bearer $TOKEN" ; echo ""
echo "--- end payment options ---"

echo ""
echo "===== WEBHOOK ROUTE EXISTS (expect 400/401, not 404) ====="
curl -s -o /dev/null -w "webhook: %{http_code}\n" -X POST https://demo.yuvaansoft.shop/api/webhooks/razorpay -H "Content-Type: application/json" -d '{}'
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

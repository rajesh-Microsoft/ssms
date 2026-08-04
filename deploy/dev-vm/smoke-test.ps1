$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV='http://172.17.0.1:8081'
DH='Host: demo.yuvaansoft.shop'

echo "===== DEV: member login ====="
TOK=$(curl -s -X POST "$DEV/api/auth/login" -H "$DH" -H 'Content-Type: application/json' \
      -d '{"username":"res-a1004","password":"demo123"}' \
      | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
[ -z "$TOK" ] && { echo "  member login FAILED"; exit 1; }
echo "  member login OK"

echo "===== DEV: payment options offered to that member ====="
curl -s "$DEV/api/member/payment-options" -H "$DH" -H "Authorization: Bearer $TOK" | head -c 600
echo ""

echo ""
echo "===== DEV: pending invoices ====="
curl -s "$DEV/api/member/pending-invoices" -H "$DH" -H "Authorization: Bearer $TOK" | head -c 400
echo ""

echo ""
echo "===== PROD: same endpoint on a real society, must NOT offer Razorpay ====="
curl -sk -o /dev/null -w '  prod webhook probe: %{http_code} (404 = Razorpay dormant)\n' \
  -X POST https://127.0.0.1/api/webhooks/razorpay -H 'Host: ssms.yuvaansoft.shop'
curl -s -o /dev/null -w '  dev  webhook probe: %{http_code} (401 = Razorpay live)\n' \
  -X POST "$DEV/api/webhooks/razorpay" -H "$DH"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

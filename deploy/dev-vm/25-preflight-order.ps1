$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV='http://172.17.0.1:8081'
DH='Host: demo.yuvaansoft.shop'

TOK=$(curl -s -X POST "$DEV/api/auth/login" -H "$DH" -H 'Content-Type: application/json' \
      -d '{"username":"res-a1004","password":"demo123"}' \
      | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
echo "member token acquired: $(echo -n "$TOK" | wc -c) chars"

echo ""
echo "===== PENDING INVOICES FOR THIS MEMBER ====="
curl -s "$DEV/api/member/pending-invoices" -H "$DH" -H "Authorization: Bearer $TOK" | head -c 600
echo ""

echo ""
echo "===== CAN THE DEV SERVER REACH RAZORPAY AND CREATE AN ORDER? ====="
curl -s -w '\nHTTP %{http_code}\n' -X POST "$DEV/api/member/razorpay/order/4488" \
  -H "$DH" -H "Authorization: Bearer $TOK" -H 'Content-Length: 0'
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

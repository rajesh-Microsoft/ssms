$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Signs a payload on the VM with the secret that is actually in .env. If this
# returns anything other than 401, the verification code is sound and the
# dashboard is simply holding a different secret. The secret never leaves the box.
$remote = @'
cd ~/smms/SMMS
SECRET=$(grep '^RAZORPAY_WEBHOOK_SECRET=' .env | cut -d= -f2 | tr -d '\r\n')
echo "secret length from .env: ${#SECRET}"

RUNNING=$(docker exec smms-api printenv Razorpay__WebhookSecret | tr -d '\r\n')
echo "secret length in running container: ${#RUNNING}"
if [ "$SECRET" = "$RUNNING" ]; then echo "MATCH: .env and container agree"; else echo "MISMATCH: container has a different value"; fi

BODY='{"event":"payment.captured","payload":{"payment":{"entity":{"id":"pay_test","order_id":"order_test","amount":100,"currency":"INR","status":"captured"}}}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | sed 's/^.*= //')

echo ""
echo "===== SELF SIGNED WEBHOOK (correct secret) ====="
curl -s -o /dev/null -w "http: %{http_code}\n" -X POST http://localhost:7253/api/webhooks/razorpay \
  -H "Host: demo.yuvaansoft.shop" -H "Content-Type: application/json" \
  -H "X-Razorpay-Signature: $SIG" -d "$BODY"

echo ""
echo "===== DELIBERATELY WRONG SIGNATURE (control) ====="
curl -s -o /dev/null -w "http: %{http_code}\n" -X POST http://localhost:7253/api/webhooks/razorpay \
  -H "Host: demo.yuvaansoft.shop" -H "Content-Type: application/json" \
  -H "X-Razorpay-Signature: deadbeef" -d "$BODY"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

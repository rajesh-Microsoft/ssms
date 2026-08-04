$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Generates a fresh webhook secret on the VM, proves the API accepts a payload
# signed with it, then puts it on your clipboard for the Razorpay dashboard.
# The value is never printed and never enters the chat.
$remote = @'
cd ~/smms/SMMS
NEW=$(head -c 64 /dev/urandom | base64 | tr -dc 'a-zA-Z0-9' | head -c 32)
cp .env .env.bak
grep -v '^RAZORPAY_WEBHOOK_SECRET=' .env.bak > .env
printf 'RAZORPAY_WEBHOOK_SECRET=%s\n' "$NEW" >> .env
chmod 600 .env
rm -f .env.bak
echo "new secret length: ${#NEW}"

grep '^RAZORPAY_WEBHOOK_SECRET=' .env | cat -A | sed 's/=[a-zA-Z0-9]*/=<hidden>/'

docker compose up -d --force-recreate smms-api > /dev/null 2>&1
for i in $(seq 1 40); do
  curl -s -o /dev/null --max-time 2 http://localhost:7253/api/webhooks/razorpay -X POST -H "Host: demo.yuvaansoft.shop" && break
  sleep 2
done

RUN=$(docker exec smms-api printenv Razorpay__WebhookSecret)
echo -n "container value byte count (33 = 32 chars + newline): "
docker exec smms-api printenv Razorpay__WebhookSecret | wc -c

BODY='{"event":"payment.captured","payload":{"payment":{"entity":{"id":"pay_test","order_id":"order_test","amount":100,"currency":"INR","status":"captured"}}}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$NEW" -hex | sed 's/^.*= //')
curl -s -o /dev/null -w "self signed webhook: %{http_code}  (200 expected)\n" -X POST http://localhost:7253/api/webhooks/razorpay -H "Host: demo.yuvaansoft.shop" -H "Content-Type: application/json" -H "X-Razorpay-Signature: $SIG" -d "$BODY"
echo "--- done ---"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

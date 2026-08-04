$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Sets the UAT webhook secret to whatever the Razorpay dashboard holds.
# The value is typed by you, travels over stdin (never argv, so it is not
# visible in the VM process list) and is never echoed.

$secure = Read-Host "Paste the webhook secret from the Razorpay dashboard" -AsSecureString
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

if ([string]::IsNullOrWhiteSpace($plain)) { throw "No secret entered." }

$installer = @'
cat > /tmp/setwh.sh <<'SCRIPT'
set -e
read -r SECRET
# PowerShell sends CRLF; a trailing CR silently becomes part of the HMAC key.
SECRET=$(printf '%s' "$SECRET" | tr -d '\r\n')
cd ~/smms/SMMS
cp .env .env.bak
grep -v '^RAZORPAY_WEBHOOK_SECRET=' .env.bak > .env
printf 'RAZORPAY_WEBHOOK_SECRET=%s\n' "$SECRET" >> .env
chmod 600 .env
rm -f .env.bak
echo "secret length written: ${#SECRET}"
docker compose up -d smms-api > /dev/null 2>&1
echo "restart exit: $?"
SCRIPT
echo "installer ready"
'@

$installer = $installer -replace "`r", ""
$installer | ssh -i $key $vm "bash -s"

$plain | ssh -i $key $vm "bash /tmp/setwh.sh; rm -f /tmp/setwh.sh"
$plain = $null

# Verifying too early gets a false 401 from the container on its way out.
$verify = @'
cd ~/smms/SMMS
for i in $(seq 1 30); do
  curl -s -o /dev/null --max-time 2 http://localhost:7253/api/webhooks/razorpay -X POST -H "Host: demo.yuvaansoft.shop" && break
  sleep 2
done
SECRET=$(grep '^RAZORPAY_WEBHOOK_SECRET=' .env | cut -d= -f2- | tr -d '\r\n')
BODY='{"event":"payment.captured","payload":{"payment":{"entity":{"id":"pay_test","order_id":"order_test","amount":100,"currency":"INR","status":"captured"}}}}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | sed 's/^.*= //')
curl -s -o /dev/null -w "self signed webhook: %{http_code}  (200 = secret in use)\n" -X POST http://localhost:7253/api/webhooks/razorpay \
  -H "Host: demo.yuvaansoft.shop" -H "Content-Type: application/json" \
  -H "X-Razorpay-Signature: $SIG" -d "$BODY"
echo "--- done ---"
'@

$verify = $verify -replace "`r", ""
$verify | ssh -i $key $vm "bash -s"

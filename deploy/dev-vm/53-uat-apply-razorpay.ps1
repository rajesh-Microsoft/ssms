$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS
echo "===== .env BYTES (CR would show as ^M) ====="
sed 's/^RAZORPAY_KEY_SECRET=.*/RAZORPAY_KEY_SECRET=<hidden>/; s/^RAZORPAY_WEBHOOK_SECRET=.*/RAZORPAY_WEBHOOK_SECRET=<hidden>/' .env | cat -A
echo "--- end env ---"

echo ""
echo "===== COMPOSE RESOLUTION ====="
docker compose config 2>/dev/null | grep -i 'razorpay__' | sed 's/\(KeySecret\|WebhookSecret\): .*/\1: <hidden>/'
echo "--- end config ---"

echo ""
echo "===== REBUILD AND RESTART ====="
docker compose build smms-api > /tmp/build.log 2>&1 ; echo "build exit: $?"
tail -n 3 /tmp/build.log
docker compose up -d smms-api > /tmp/up.log 2>&1 ; echo "up exit: $?"
cat /tmp/up.log
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

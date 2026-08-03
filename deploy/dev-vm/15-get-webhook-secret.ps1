$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== DEV Razorpay config (from ~/smms-dev/SMMS/.env) ====="
grep -E '^RAZORPAY_' ~/smms-dev/SMMS/.env

echo ""
echo "===== What the running dev API actually loaded ====="
docker inspect smms-dev-api --format '{{range .Config.Env}}{{println .}}{{end}}' | grep -i razorpay
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

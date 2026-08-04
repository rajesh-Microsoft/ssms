$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$sha = (git rev-parse main).Trim()
$short = (git rev-parse --short main).Trim()
$tgz = Join-Path $env:TEMP "uat-ship-$short.tgz"

Write-Host "Shipping main = $sha"
git archive --format=tar.gz -o $tgz main
scp -i $key $tgz "${vm}:/tmp/uat-ship.tgz"
Remove-Item $tgz

# The webhook secret is generated on the VM and never printed. Reveal it with:
#   ssh ... "grep RAZORPAY_WEBHOOK_SECRET ~/smms/SMMS/.env"
$remote = @"
set -e
cd ~/smms/SMMS

rm -f api/SMMS.Api/local-nuget/*.nupkg
tar xzf /tmp/uat-ship.tgz -C ~/smms/SMMS --overwrite
rm -f /tmp/uat-ship.tgz
echo "$sha" > ~/smms/SMMS/DEPLOYED_COMMIT
echo "DEPLOYED_COMMIT is now:"
cat ~/smms/SMMS/DEPLOYED_COMMIT

echo ""
echo "===== WRITING .env (demo society only, test keys) ====="
if [ -f .env ]; then cp .env .env.bak; fi
WEBHOOK_SECRET=`$(openssl rand -hex 24)
umask 077
cat > .env <<EOF
RAZORPAY_ENABLED=true
RAZORPAY_KEY_ID=rzp_test_TLJao9mugdVcYl
RAZORPAY_KEY_SECRET=TfeKM0oUSstf2kE03MEV4dpK
RAZORPAY_WEBHOOK_SECRET=`$WEBHOOK_SECRET
RAZORPAY_ALLOWED_SOCIETIES=demo
EOF
chmod 600 .env
ls -la .env
echo ""
echo "--- .env contents, webhook secret masked ---"
sed 's/^RAZORPAY_WEBHOOK_SECRET=.*/RAZORPAY_WEBHOOK_SECRET=<generated, 48 hex chars, not shown>/' .env
"@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

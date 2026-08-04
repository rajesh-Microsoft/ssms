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

# Never leave a numeric argument or filename at the end of a line: the CR that
# survives the pipe turns "tail -n 12" into "tail -n 12\r" and it fails.
$remote = @"
set -e
cd ~/smms/SMMS

rm -f api/SMMS.Api/local-nuget/*.nupkg
tar xzf /tmp/uat-ship.tgz -C ~/smms/SMMS --overwrite
rm -f /tmp/uat-ship.tgz
echo "$sha" > ~/smms/SMMS/DEPLOYED_COMMIT
echo -n "DEPLOYED_COMMIT: " ; cat ~/smms/SMMS/DEPLOYED_COMMIT

echo ""
echo "===== ALLOWLIST: add aadya alongside demo ====="
cp .env .env.bak
sed -i 's/^RAZORPAY_ALLOWED_SOCIETIES=.*/RAZORPAY_ALLOWED_SOCIETIES=demo,aadya/' .env
grep '^RAZORPAY_ALLOWED_SOCIETIES=' .env ; echo "--- end allowlist ---"

echo ""
echo "===== BUILD ====="
docker compose build smms-api > /tmp/build.log 2>&1 ; echo "build exit: `$?"
tail -n 12 /tmp/build.log ; echo "--- end build tail ---"

echo ""
echo "===== RECREATE API (picks up .env + runs the new migration) ====="
docker compose up -d --force-recreate smms-api > /tmp/up.log 2>&1 ; echo "up exit: `$?"
cat /tmp/up.log ; echo "--- end up ---"
"@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"
Write-Host "--- done ---"

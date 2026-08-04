$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$sha = (git rev-parse main).Trim()
$short = (git rev-parse --short main).Trim()
$tgz = Join-Path $env:TEMP "dev-ship-$short.tgz"

Write-Host "Shipping main = $sha to the dev stack"
git archive --format=tar.gz -o $tgz main
scp -i $key $tgz "${vm}:/tmp/dev-ship.tgz"
Remove-Item $tgz

# Never leave a numeric argument or filename at the end of a line: the CR that
# survives the pipe turns "tail -n 12" into "tail -n 12\r" and it fails.
$remote = @"
set -e
cd ~/smms-dev/SMMS

rm -f api/SMMS.Api/local-nuget/*.nupkg
tar xzf /tmp/dev-ship.tgz -C ~/smms-dev/SMMS --overwrite
rm -f /tmp/dev-ship.tgz
echo "$sha" > ~/smms-dev/SMMS/DEPLOYED_COMMIT
echo -n "DEPLOYED_COMMIT: " ; cat ~/smms-dev/SMMS/DEPLOYED_COMMIT

echo ""
echo "===== DEV ALLOWLIST (unchanged, dev allows every society) ====="
grep '^RAZORPAY_ALLOWED_SOCIETIES=' .env ; echo "--- end allowlist ---"

echo ""
echo "===== BUILD ====="
docker compose build smms-api > /tmp/devbuild.log 2>&1 ; echo "build exit: `$?"
tail -n 8 /tmp/devbuild.log ; echo "--- end build tail ---"

echo ""
echo "===== RECREATE API (runs the new migration on every dev tenant) ====="
docker compose up -d --force-recreate smms-api > /tmp/devup.log 2>&1 ; echo "up exit: `$?"
cat /tmp/devup.log ; echo "--- end up ---"
"@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"
Write-Host "--- done ---"

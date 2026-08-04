$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'
$stamp = Get-Date -Format 'yyyyMMddTHHmmssZ'

# The VM cannot fetch from GitHub (private repo, no credentials on the box), so
# UAT gets its code the same way dev does: an archive of an exact commit.
$sha = (git rev-parse main).Trim()
$short = (git rev-parse --short main).Trim()
$tgz = Join-Path $env:TEMP "uat-ship-$short.tgz"

Write-Host "Shipping main = $sha"
git archive --format=tar.gz -o $tgz main
Write-Host ("archive: {0:N1} MB" -f ((Get-Item $tgz).Length / 1MB))

scp -i $key $tgz "${vm}:/tmp/uat-ship.tgz"
Remove-Item $tgz

$remote = @"
set -e
cd ~/smms/SMMS

echo "===== TAGGING ROLLBACK IMAGE ====="
docker tag smms-smms-api smms-smms-api:rollback-$stamp || true

echo ""
echo "===== EXTRACTING $short OVER THE TREE ====="
# The local-nuget blobs refuse to be clobbered in place, and they are byte
# identical anyway, so clear them first and let the archive lay them down fresh.
rm -f api/SMMS.Api/local-nuget/*.nupkg
tar xzf /tmp/uat-ship.tgz -C ~/smms/SMMS --overwrite
rm -f /tmp/uat-ship.tgz
echo "$sha" > ~/smms/SMMS/DEPLOYED_COMMIT
echo "DEPLOYED_COMMIT is now:"
cat ~/smms/SMMS/DEPLOYED_COMMIT

echo ""
echo "===== RAZORPAY FILES NOW PRESENT ====="
ls -la api/SMMS.Api/Services/Payments/RazorpayPaymentGateway.cs
ls -la api/SMMS.Api/Controllers/RazorpayWebhookController.cs

echo ""
echo "===== RAZORPAY CONFIG RESOLVES TO DISABLED (no .env) ====="
ls .env 2>&1 | tail -n 1
docker compose config 2>/dev/null | grep -i 'razorpay__' || echo "(no razorpay env resolved)"

echo ""
echo "===== BUILDING API ====="
docker compose build smms-api 2>&1 | tail -n 15
"@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

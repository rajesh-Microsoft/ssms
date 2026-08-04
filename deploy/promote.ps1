<#
.SYNOPSIS
Promotes an exact commit along local -> dev -> UAT.

.DESCRIPTION
UAT hosts societies with real money in them, so a commit may only land there after
it has been deployed to dev AND signed off on dev at that same commit. The sign-off
is recorded on the dev box as DEPLOYED_VERIFIED and is cleared automatically by the
next dev deploy, so it can never outlive the code it refers to.

.EXAMPLE
  ./deploy/promote.ps1 -Environment dev
  # ...test on dev...
  ./deploy/promote.ps1 -Environment dev -MarkVerified
  ./deploy/promote.ps1 -Environment uat
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev', 'uat')][string]$Environment,
    [string]$Commit = 'main',
    [switch]$MarkVerified,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'
$stamp = Get-Date -Format 'yyyyMMddTHHmmssZ'

$targets = @{
    dev = @{
        Root    = '~/smms-dev/SMMS'
        Image   = 'smms-dev-smms-api'
        Api     = 'smms-dev-api'
        Health  = 'http://172.17.0.1:8081/'
        Restore = 'cp deploy/dev-vm/docker-compose.yml docker-compose.yml'
        Guard   = "if ! grep -q '^name: smms-dev' docker-compose.yml; then echo 'ABORT: dev compose file missing, refusing to run compose'; exit 1; fi"
    }
    uat = @{
        Root    = '~/smms/SMMS'
        Image   = 'smms-smms-api'
        Api     = 'smms-api'
        Health  = 'https://demo.yuvaansoft.shop/'
        Restore = 'true'
        Guard   = "if grep -q '^name:' docker-compose.yml; then echo 'ABORT: the dev compose file landed on UAT'; exit 1; fi`nif ! grep -q 'container_name: smms-api' docker-compose.yml; then echo 'ABORT: unrecognised compose file'; exit 1; fi"
    }
}
$t = $targets[$Environment]

function Invoke-Remote([string]$Script) {
    # A CR survives onto the last line of anything piped to ssh, so every script ends
    # with a marker line that is harmless if it arrives as "--- done ---\r".
    ($Script -replace "`r", '') | ssh -i $key $vm 'bash -s'
    if ($LASTEXITCODE -ne 0) { throw "remote script failed with exit code $LASTEXITCODE" }
}

$sha = (git rev-parse $Commit).Trim()
$short = $sha.Substring(0, 7)

# ── Sign-off mode: record that this commit passed testing where it is deployed ──
if ($MarkVerified) {
    Write-Host "Marking $short verified on $Environment" -ForegroundColor Cyan
    Invoke-Remote @"
cd $($t.Root)
deployed=`$(cat DEPLOYED_COMMIT 2>/dev/null | tr -d '\r\n')
if [ "`$deployed" != "$sha" ]; then
  echo "ABORT: $Environment is running `$deployed, not $sha"
  exit 1
fi
echo "$sha" > DEPLOYED_VERIFIED
echo "$Environment signed off at:"
cat DEPLOYED_VERIFIED
echo "--- done ---"
"@
    Write-Host "`n$Environment verified at $short." -ForegroundColor Green
    if ($Environment -eq 'dev') { Write-Host "UAT promotion of this commit is now unlocked." }
    return
}

# ── Gates ──
Write-Host "Promoting $short to $Environment" -ForegroundColor Cyan

$dirty = git status --porcelain
if ($dirty -and -not $Force) {
    throw "Working tree has uncommitted changes. Commit them, or pass -Force to ship $short anyway."
}

if (-not $Force) {
    git branch -r --contains $sha 2>$null | Where-Object { $_ -match 'origin/main' } | Out-Null
    if (-not (git branch -r --contains $sha 2>$null | Select-String 'origin/main' -Quiet)) {
        throw "$short is not on origin/main. Push it first so the deployed code is recoverable."
    }
}

if ($Environment -eq 'uat' -and -not $Force) {
    Write-Host 'Checking the dev sign-off...'
    $verified = (ssh -i $key $vm "cat ~/smms-dev/SMMS/DEPLOYED_VERIFIED 2>/dev/null" | Out-String).Trim()
    if ($verified -ne $sha) {
        $state = if ($verified) { "dev is signed off at $($verified.Substring(0,7))" } else { 'dev has no sign-off recorded' }
        throw @"
Refusing to promote to UAT: $state, not $short.
UAT carries societies with real balances. Deploy to dev, test it, then run:
  ./deploy/promote.ps1 -Environment dev -MarkVerified
"@
    }
    Write-Host "dev is signed off at $short." -ForegroundColor Green
}

# ── Ship ──
$tgz = Join-Path $env:TEMP "promote-$short.tgz"
git archive --format=tar.gz -o $tgz $sha
Write-Host ("archive: {0:N1} MB" -f ((Get-Item $tgz).Length / 1MB))
scp -i $key $tgz "${vm}:/tmp/promote.tgz"
Remove-Item $tgz

Invoke-Remote @"
set -e
cd $($t.Root)

echo "===== ROLLBACK IMAGE ====="
docker tag $($t.Image) $($t.Image):rollback-$stamp || true
echo "rollback tag: $($t.Image):rollback-$stamp"

echo ""
echo "===== EXTRACTING $short ====="
# The local-nuget blobs refuse to be clobbered in place and are byte identical anyway.
rm -f api/SMMS.Api/local-nuget/*.nupkg
tar xzf /tmp/promote.tgz -C $($t.Root) --overwrite
rm -f /tmp/promote.tgz

# The archive always carries the PRODUCTION compose file at the repo root. On dev it must
# be swapped back before any compose command runs, or the project name falls back to the
# directory name and compose recreates the UAT containers using this stack's .env.
$($t.Restore)
$($t.Guard)
echo -n "compose project: " ; grep -m1 '^name:' docker-compose.yml || echo "(none, defaults to directory)"

echo "$sha" > DEPLOYED_COMMIT
rm -f DEPLOYED_VERIFIED
echo -n "DEPLOYED_COMMIT: " ; cat DEPLOYED_COMMIT

echo ""
echo "===== RAZORPAY ALLOWLIST (unchanged by deploys) ====="
grep '^RAZORPAY_ALLOWED_SOCIETIES=' .env 2>/dev/null || echo "(none)"
echo "--- end allowlist ---"

echo ""
echo "===== BUILD ====="
docker compose build smms-api > /tmp/promote-build.log 2>&1 ; echo "build exit: `$?"
tail -n 8 /tmp/promote-build.log
echo "--- end build ---"

echo ""
echo "===== RECREATE API (applies pending migrations to every tenant) ====="
docker compose up -d --force-recreate smms-api > /tmp/promote-up.log 2>&1 ; echo "up exit: `$?"
cat /tmp/promote-up.log

echo ""
echo "===== HEALTH ====="
# The site root is static content served by nginx and answers 200 even when the API is
# down, so liveness is judged on an API route instead. Unauthenticated /api/me returns
# 401 from the API but 502 when it is not accepting connections yet.
api=000
for i in 1 2 3 4 5 6 7 8 9 10; do
  api=`$(curl -s -o /dev/null -w '%{http_code}' $($t.Health)api/me || true)
  case "`$api" in 200|401|403) break;; esac
  sleep 6
done
site=`$(curl -s -o /dev/null -w '%{http_code}' $($t.Health) || true)
echo "site  -> `$site"
echo "api   -> `$api  (401 means up and rejecting anonymous callers)"
echo -n "tenants migrated: "
docker logs $($t.Api) 2>&1 | grep -c 'Database is ready' || true
echo "exceptions:"
docker logs $($t.Api) 2>&1 | grep -iE 'unhandled|exception|fail: ' | head -n 5 || true
echo "(nothing above means clean)"
case "`$api" in 200|401|403) ;; *) echo "ABORT: API unhealthy"; exit 1;; esac
docker ps --filter "name=$($t.Api)" --format '{{.Names}}  {{.Status}}'
echo "--- done ---"
"@

Write-Host "`n$short is live on $Environment." -ForegroundColor Green
if ($Environment -eq 'dev') {
    Write-Host "Test it, then sign off:  ./deploy/promote.ps1 -Environment dev -MarkVerified"
}
else {
    Write-Host "Roll back with:  docker tag $($t.Image):rollback-$stamp $($t.Image) && docker compose up -d --force-recreate smms-api"
}

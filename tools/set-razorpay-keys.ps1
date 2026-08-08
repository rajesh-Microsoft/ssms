<#
.SYNOPSIS
Sets the Razorpay credentials on the dev stack without the values being typed on a command
line, echoed, or written to shell history.

.DESCRIPTION
Live keys move real money. They are read with Read-Host -AsSecureString, sent over ssh on
stdin (never as arguments, which are visible in `ps`), and written to ~/smms-dev/SMMS/.env
with mode 600.

The society allowlist is narrowed at the same time. Dev carries clones of real societies,
so a live key on that box with an allowlist of "*" would let a real resident pay a dev
invoice with real money.

.EXAMPLE
  ./tools/set-razorpay-keys.ps1 -Mode live -AllowedSocieties demo
  ./tools/set-razorpay-keys.ps1 -Clear
#>
[CmdletBinding(DefaultParameterSetName = 'Set')]
param(
    [Parameter(ParameterSetName = 'Set')][ValidateSet('live', 'test')][string]$Mode = 'live',
    [Parameter(ParameterSetName = 'Set')][string]$AllowedSocieties = 'demo',
    [Parameter(ParameterSetName = 'Clear')][switch]$Clear,
    [string]$Vm = '135.235.195.132',
    [string]$User = 'ssmsadmin',
    [string]$KeyPath = "$env:USERPROFILE\ssmsadmin.pem"
)
$ErrorActionPreference = 'Stop'
$envPath = '~/smms-dev/SMMS/.env'

function Read-Plain([string]$Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    [System.Net.NetworkCredential]::new('', $secure).Password
}
function Send-Remote([string]$Script) {
    ($Script -replace "`r", "") | ssh -i $KeyPath -o BatchMode=yes -o StrictHostKeyChecking=accept-new "$User@$Vm" 'bash -s'
    if ($LASTEXITCODE -ne 0) { throw "remote step failed (is the JIT window open?)" }
}

if ($Clear) {
    Write-Host 'Removing Razorpay credentials from dev and disabling the gateway.' -ForegroundColor Cyan
    Send-Remote @"
set -e
cd ~/smms-dev/SMMS
cp .env .env.bak-`$(date -u +%Y%m%dT%H%M%SZ)
sed -i 's/^RAZORPAY_ENABLED=.*/RAZORPAY_ENABLED=false/' .env
sed -i 's/^RAZORPAY_KEY_ID=.*/RAZORPAY_KEY_ID=/' .env
sed -i 's/^RAZORPAY_KEY_SECRET=.*/RAZORPAY_KEY_SECRET=/' .env
sed -i 's/^RAZORPAY_WEBHOOK_SECRET=.*/RAZORPAY_WEBHOOK_SECRET=/' .env
chmod 600 .env
docker compose up -d --force-recreate smms-api >/dev/null 2>&1
sleep 4
KID=`$(docker exec smms-dev-api printenv Razorpay__KeyId 2>/dev/null)
echo "RAZORPAY_ENABLED now: `$(docker exec smms-dev-api printenv Razorpay__Enabled 2>/dev/null)"
echo "key id length now:    `${#KID}  (0 means cleared)"
"@
    Write-Host 'Cleared. Remember to delete the webhook in the Razorpay dashboard too.' -ForegroundColor Green
    return
}

Write-Host "Setting $Mode Razorpay keys on DEV, allowlist '$AllowedSocieties'." -ForegroundColor Cyan
if ($Mode -eq 'live') {
    Write-Warning 'These keys move REAL money. Keep the test amount small and refund it afterwards.'
}

$keyId = Read-Plain 'Razorpay Key ID'
$keySecret = Read-Plain 'Razorpay Key Secret'
$hookSecret = Read-Plain 'Razorpay Webhook Secret (the one you set in the dashboard)'

$expected = "rzp_${Mode}_"
if (-not $keyId.StartsWith($expected)) { throw "That key id does not start with '$expected', so it is not a $Mode key." }
if ([string]::IsNullOrWhiteSpace($keySecret)) { throw 'The key secret is empty.' }
if ([string]::IsNullOrWhiteSpace($hookSecret)) { throw 'The webhook secret is empty.' }

# Values travel on stdin inside a quoted here-doc: nothing lands in argv or shell history.
Send-Remote @"
set -e
cd ~/smms-dev/SMMS
cp .env .env.bak-`$(date -u +%Y%m%dT%H%M%SZ)
python3 - <<'PY'
import re
vals = {
  'RAZORPAY_ENABLED': 'true',
  'RAZORPAY_KEY_ID': '''$keyId''',
  'RAZORPAY_KEY_SECRET': '''$keySecret''',
  'RAZORPAY_WEBHOOK_SECRET': '''$hookSecret''',
  'RAZORPAY_ALLOWED_SOCIETIES': '''$AllowedSocieties''',
}
lines = open('.env').read().splitlines()
seen = set()
out = []
for line in lines:
    m = re.match(r'^([A-Za-z_][A-Za-z0-9_]*)=', line)
    if m and m.group(1) in vals:
        out.append(f"{m.group(1)}={vals[m.group(1)]}")
        seen.add(m.group(1))
    else:
        out.append(line)
for k, v in vals.items():
    if k not in seen:
        out.append(f"{k}={v}")
open('.env', 'w').write("\n".join(out) + "\n")
PY
chmod 600 .env
docker compose up -d --force-recreate smms-api >/dev/null 2>&1
sleep 5
echo "--- as the container sees it (no secrets printed) ---"
echo "  enabled:   `$(docker exec smms-dev-api printenv Razorpay__Enabled)"
echo "  allowlist: `$(docker exec smms-dev-api printenv Razorpay__AllowedSocieties)"
KID=`$(docker exec smms-dev-api printenv Razorpay__KeyId)
echo "  key id:    `${KID:0:9}... (`${#KID} chars)"
SEC=`$(docker exec smms-dev-api printenv Razorpay__KeySecret)
echo "  secret:    `${#SEC} chars"
WH=`$(docker exec smms-dev-api printenv Razorpay__WebhookSecret)
echo "  webhook:   `${#WH} chars"
echo "  unsigned webhook POST -> `$(curl -s -o /dev/null -w '%{http_code}' --resolve dev-ssms.yuvaansoft.shop:443:127.0.0.1 -X POST https://dev-ssms.yuvaansoft.shop/api/webhooks/razorpay -H 'Content-Type: application/json' -d '{}')  (401 expected)"
"@

$keyId = $keySecret = $hookSecret = $null
[System.GC]::Collect()
Write-Host 'Done. Run with -Clear to strip the live keys off dev when you have finished.' -ForegroundColor Green

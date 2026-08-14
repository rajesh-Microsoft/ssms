<#
.SYNOPSIS
Sets the PhonePe sandbox credentials on the dev stack without the values being typed on a
command line, echoed, or written to shell history.

.DESCRIPTION
Credentials are read with Read-Host -AsSecureString, sent over ssh on stdin (never as
arguments, which are visible in `ps`), and written to ~/smms-dev/SMMS/.env with mode 600.

The society allowlist is narrowed at the same time. Dev carries clones of real societies, so
an allowlist of "*" would expose real tenants to a gateway that is still being tested.

After writing, it proves the credentials actually work by calling the PhonePe token endpoint
from the VM and printing only the HTTP status and token type - never the token itself.

.EXAMPLE
  ./tools/set-phonepe-keys.ps1 -AllowedSocieties demo -GenerateWebhookPassword
  ./tools/set-phonepe-keys.ps1 -Clear
#>
[CmdletBinding(DefaultParameterSetName = 'Set')]
param(
    [Parameter(ParameterSetName = 'Set')][ValidateSet('Sandbox', 'Production')][string]$Environment = 'Sandbox',
    [Parameter(ParameterSetName = 'Set')][string]$AllowedSocieties = 'demo',
    [Parameter(ParameterSetName = 'Set')][string]$WebhookUsername = 'smms',
    [Parameter(ParameterSetName = 'Set')][switch]$GenerateWebhookPassword,
    [Parameter(ParameterSetName = 'Clear')][switch]$Clear,
    [string]$Vm = '135.235.195.132',
    [string]$User = 'ssmsadmin',
    [string]$KeyPath = "$env:USERPROFILE\ssmsadmin.pem"
)
$ErrorActionPreference = 'Stop'

function Read-Plain([string]$Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    [System.Net.NetworkCredential]::new('', $secure).Password
}
function Send-Remote([string]$Script) {
    ($Script -replace "`r", "") | ssh -i $KeyPath -o BatchMode=yes -o StrictHostKeyChecking=accept-new "$User@$Vm" 'bash -s'
    if ($LASTEXITCODE -ne 0) { throw "remote step failed (is the JIT window open?)" }
}

if ($Clear) {
    Write-Host 'Removing PhonePe credentials from dev and disabling the gateway.' -ForegroundColor Cyan
    Send-Remote @"
set -e
cd ~/smms-dev/SMMS
cp .env .env.bak-`$(date -u +%Y%m%dT%H%M%SZ)
for k in PHONEPE_ENABLED PHONEPE_CLIENT_ID PHONEPE_CLIENT_SECRET PHONEPE_WEBHOOK_USERNAME PHONEPE_WEBHOOK_PASSWORD PHONEPE_ALLOWED_SOCIETIES; do
  sed -i "s/^`${k}=.*/`${k}=/" .env
done
sed -i 's/^PHONEPE_ENABLED=.*/PHONEPE_ENABLED=false/' .env
chmod 600 .env
docker compose up -d --force-recreate smms-api >/dev/null 2>&1
sleep 4
CID=`$(docker exec smms-dev-api printenv PhonePe__ClientId 2>/dev/null)
echo "PHONEPE_ENABLED now:  `$(docker exec smms-dev-api printenv PhonePe__Enabled 2>/dev/null)"
echo "client id length now: `${#CID}  (0 means cleared)"
"@
    Write-Host 'Cleared. Ask your PhonePe POC to disable the webhook too.' -ForegroundColor Green
    return
}

Write-Host "Setting PhonePe $Environment credentials on DEV, allowlist '$AllowedSocieties'." -ForegroundColor Cyan
if ($Environment -eq 'Production') {
    Write-Warning 'Production credentials move REAL money. Keep the test amount small and refund it afterwards.'
}

$clientId = Read-Plain 'PhonePe Client ID'
$clientSecret = Read-Plain 'PhonePe Client Secret'
$clientVersion = Read-Host -Prompt 'PhonePe Client Version (press Enter for 1)'
if ([string]::IsNullOrWhiteSpace($clientVersion)) { $clientVersion = '1' }

if ($GenerateWebhookPassword) {
    $bytes = [byte[]]::new(24)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $webhookPassword = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('/', '_').Replace('+', '-')
    Write-Host ''
    Write-Host 'Generated webhook password (shown once - send it to your PhonePe POC):' -ForegroundColor Yellow
    Write-Host "  username: $WebhookUsername"
    Write-Host "  password: $webhookPassword"
    Write-Host ''
}
else {
    $webhookPassword = Read-Plain 'PhonePe Webhook Password (the one configured with your POC)'
}

if ([string]::IsNullOrWhiteSpace($clientId)) { throw 'The client id is empty.' }
if ([string]::IsNullOrWhiteSpace($clientSecret)) { throw 'The client secret is empty.' }
if ([string]::IsNullOrWhiteSpace($webhookPassword)) { throw 'The webhook password is empty.' }

# Values travel on stdin inside a quoted here-doc: nothing lands in argv or shell history.
Send-Remote @"
set -e
cd ~/smms-dev/SMMS
cp .env .env.bak-`$(date -u +%Y%m%dT%H%M%SZ)
python3 - <<'PY'
import re
vals = {
  'PHONEPE_ENABLED': 'true',
  'PHONEPE_ENVIRONMENT': '''$Environment''',
  'PHONEPE_CLIENT_ID': '''$clientId''',
  'PHONEPE_CLIENT_SECRET': '''$clientSecret''',
  'PHONEPE_CLIENT_VERSION': '''$clientVersion''',
  'PHONEPE_WEBHOOK_USERNAME': '''$WebhookUsername''',
  'PHONEPE_WEBHOOK_PASSWORD': '''$webhookPassword''',
  'PHONEPE_ALLOWED_SOCIETIES': '''$AllowedSocieties''',
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
sleep 6

echo "--- as the container sees it (no secrets printed) ---"
echo "  enabled:     `$(docker exec smms-dev-api printenv PhonePe__Enabled)"
echo "  environment: `$(docker exec smms-dev-api printenv PhonePe__Environment)"
echo "  allowlist:   `$(docker exec smms-dev-api printenv PhonePe__AllowedSocieties)"
CID=`$(docker exec smms-dev-api printenv PhonePe__ClientId)
echo "  client id:   `${CID:0:4}... (`${#CID} chars)"
SEC=`$(docker exec smms-dev-api printenv PhonePe__ClientSecret)
echo "  secret:      `${#SEC} chars"
WHP=`$(docker exec smms-dev-api printenv PhonePe__WebhookPassword)
echo "  webhook pwd: `${#WHP} chars"

echo "--- do the credentials actually work? ---"
CVER=`$(docker exec smms-dev-api printenv PhonePe__ClientVersion)
for HOST in pg-sandbox pgsandbox; do
  CODE=`$(curl -s -o /tmp/pp.json -w '%{http_code}' \
    -X POST "https://api-preprod.phonepe.com/apis/`${HOST}/v1/oauth/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode "client_id=`$CID" \
    --data-urlencode "client_secret=`$SEC" \
    --data-urlencode "client_version=`$CVER" \
    --data-urlencode 'grant_type=client_credentials' || echo 000)
  TYPE=`$(grep -o '"token_type":"[^"]*"' /tmp/pp.json 2>/dev/null || true)
  echo "  /apis/`${HOST}/v1/oauth/token -> `$CODE `$TYPE"
  rm -f /tmp/pp.json
done

echo "--- webhook endpoint ---"
echo "  unauthenticated POST -> `$(curl -s -o /dev/null -w '%{http_code}' --resolve dev-ssms.yuvaansoft.shop:443:127.0.0.1 -X POST https://dev-ssms.yuvaansoft.shop/api/webhooks/phonepe -H 'Content-Type: application/json' -d '{}')  (401 expected once credentials are set)"
"@

$clientId = $clientSecret = $webhookPassword = $null
[System.GC]::Collect()

Write-Host ''
Write-Host 'Next: enable online payments for the society, then send the webhook URL to your POC.' -ForegroundColor Green
Write-Host '  URL:      https://dev-ssms.yuvaansoft.shop/api/webhooks/phonepe'
Write-Host '  Auth:     SHA'
Write-Host "  Username: $WebhookUsername"
Write-Host '  Events:   checkout.order.completed, checkout.order.failed'

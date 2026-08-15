<#
.SYNOPSIS
Sets the PhonePe sandbox credentials on the dev or pre-prod stack without the values being
typed on a command line, echoed, or written to shell history.

.DESCRIPTION
Credentials are read with Read-Host -AsSecureString (or from a shredded file), sent to the box
on stdin - never as arguments, which are visible in `ps` - and written to that stack's .env
with mode 600.

Transport differs per target. Dev goes over ssh. Pre-prod has no reachable ssh from this
workstation, so it goes through `az vm run-command`, which writes the script it ran to
/var/lib/waagent/run-command/download on the VM; the script therefore shreds those copies
before it exits. That residue is why Production credentials are refused on pre-prod.

The society allowlist is narrowed at the same time. Both boxes carry clones of real societies,
so an allowlist of "*" would expose real tenants to a gateway that is still being tested.

After writing, it proves the credentials actually work by calling the PhonePe token endpoint
from the VM and printing only the HTTP status and token type - never the token itself.

.EXAMPLE
  ./tools/set-phonepe-keys.ps1 -AllowedSocieties demo -GenerateWebhookPassword
  ./tools/set-phonepe-keys.ps1 -Target PProd -AllowedSocieties aadya -CredentialFile $env:TEMP\pp.txt
  ./tools/set-phonepe-keys.ps1 -Target PProd -Clear
#>
[CmdletBinding(DefaultParameterSetName = 'Set')]
param(
    [Parameter(ParameterSetName = 'Set')][ValidateSet('Sandbox', 'Production')][string]$Environment = 'Sandbox',
    [Parameter(ParameterSetName = 'Set')][string]$AllowedSocieties = 'demo',
    [Parameter(ParameterSetName = 'Set')][string]$WebhookUsername = 'smms',
    [Parameter(ParameterSetName = 'Set')][switch]$GenerateWebhookPassword,
    # Secure prompts swallow clipboard pastes in some terminals, which silently stores a
    # one-character credential. Point this at a file of KEY=VALUE lines instead; it is shredded
    # after reading. Keys: CLIENT_ID, CLIENT_SECRET, optional CLIENT_VERSION, WEBHOOK_PASSWORD.
    [Parameter(ParameterSetName = 'Set')][string]$CredentialFile,
    [Parameter(ParameterSetName = 'Clear')][switch]$Clear,
    [ValidateSet('Dev', 'PProd')][string]$Target = 'Dev',
    [string]$Vm = '135.235.195.132',
    [string]$User = 'ssmsadmin',
    [string]$KeyPath = "$env:USERPROFILE\ssmsadmin.pem"
)
$ErrorActionPreference = 'Stop'

# Pre-prod lives on the MSDN subscription and is reachable only through the Azure agent.
$pprodSub = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d'
$pprodRg = 'smms-pprod-rg'
$pprodVm = 'smms-pprod-vm'

$targets = @{
    Dev   = @{
        Transport   = 'ssh'
        Root        = '~/smms-dev/SMMS'
        Api         = 'smms-dev-api'
        Project     = 'smms-dev'
        WebhookHost = 'dev-ssms.yuvaansoft.shop'
        Own         = 'true'
    }
    PProd = @{
        Transport   = 'runcommand'
        Root        = '/opt/smms/app'
        Api         = 'smms-pprod-api'
        Project     = 'smms-pprod'
        WebhookHost = 'pprod-ssms.ssms.yuvaansoft.shop'
        # run-command runs as root; hand .env back or the stack stops being drivable as ssmsadmin.
        Own         = 'chown ssmsadmin:ssmsadmin /opt/smms/app/.env*'
    }
}
$t = $targets[$Target]

# run-command writes the script it ran - credentials and all - to disk under waagent. It has
# to be wiped by a SEPARATE invocation: a background job inside the same script is killed when
# the handler reaps it, and backgrounding also stops the handler seeing the completion marker.
function Clear-Residue {
    if ($t.Transport -ne 'runcommand') { return }
    Send-Remote @'
find /var/lib/waagent/run-command/download -name script.sh -mmin +1 -exec grep -l PHONEPE_CLIENT_SECRET {} \; 2>/dev/null |
  while read -r f; do shred -z -n 1 "$f" && echo "wiped $f"; done
echo "scripts still holding a secret: $(find /var/lib/waagent/run-command/download -name script.sh -mmin +1 -exec grep -l PHONEPE_CLIENT_SECRET {} \; 2>/dev/null | wc -l)"
'@
}

function Read-Plain([string]$Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    [System.Net.NetworkCredential]::new('', $secure).Password
}

# run-command reports "Enable succeeded" even when the script inside it failed, so a marker
# echoed on the last line is the only trustworthy completion signal on either transport.
function Send-Remote([string]$Script) {
    $body = ($Script -replace "`r", "") + "`necho __PP_OK__`n"
    if ($t.Transport -eq 'ssh') {
        $out = ($body | ssh -i $KeyPath -o BatchMode=yes -o StrictHostKeyChecking=accept-new "$User@$Vm" 'bash -s' 2>&1 | Out-String)
    }
    else {
        $tmp = New-TemporaryFile
        [System.IO.File]::WriteAllText($tmp.FullName, $body)
        $raw = (az vm run-command invoke --subscription $pprodSub -g $pprodRg -n $pprodVm `
                --command-id RunShellScript --scripts "@$($tmp.FullName)" -o json 2>&1 | Out-String)
        # That temp file held the credentials in the clear; overwrite before unlinking.
        [System.IO.File]::WriteAllText($tmp.FullName, ('x' * $body.Length))
        Remove-Item $tmp.FullName -ErrorAction SilentlyContinue
        $out = $raw
        try { $out = (($raw | ConvertFrom-Json).value | ForEach-Object { $_.message }) -join "`n" } catch { }
        if ($out -match '(?s)\[stdout\](.*?)(\[stderr\]|$)') { $out = $Matches[1] }
    }
    Write-Host $out
    if ($out -notmatch '__PP_OK__') { throw "remote step on $Target did not run to completion (is the JIT window open?)" }
}

if ($Clear) {
    Write-Host "Removing PhonePe credentials from $Target and disabling the gateway." -ForegroundColor Cyan
    Send-Remote @"
set -e
cd $($t.Root)
grep -q '^name: $($t.Project)' docker-compose.yml || { echo 'ABORT: wrong compose file in this directory'; exit 1; }
cp .env .env.bak-`$(date -u +%Y%m%dT%H%M%SZ)
for k in PHONEPE_ENABLED PHONEPE_CLIENT_ID PHONEPE_CLIENT_SECRET PHONEPE_WEBHOOK_USERNAME PHONEPE_WEBHOOK_PASSWORD PHONEPE_ALLOWED_SOCIETIES; do
  sed -i "s/^`${k}=.*/`${k}=/" .env
done
sed -i 's/^PHONEPE_ENABLED=.*/PHONEPE_ENABLED=false/' .env
chmod 600 .env
$($t.Own)
docker compose up -d --force-recreate smms-api >/dev/null 2>&1
sleep 4
CID=`$(docker exec $($t.Api) printenv PhonePe__ClientId 2>/dev/null)
echo "PHONEPE_ENABLED now:  `$(docker exec $($t.Api) printenv PhonePe__Enabled 2>/dev/null)"
echo "client id length now: `${#CID}  (0 means cleared)"
"@
    Clear-Residue
    Write-Host 'Cleared. Ask your PhonePe POC to disable the webhook too.' -ForegroundColor Green
    return
}

Write-Host "Setting PhonePe $Environment credentials on $Target, allowlist '$AllowedSocieties'." -ForegroundColor Cyan
if ($Environment -eq 'Production') {
    if ($Target -ne 'Dev') {
        throw "Refusing to put Production credentials on $Target. Only the real production stack may hold live keys."
    }
    Write-Warning 'Production credentials move REAL money. Keep the test amount small and refund it afterwards.'
}
if ($Target -eq 'PProd') {
    Write-Warning 'Pre-prod carries a CLONE of live society data. A sandbox payment here will mark a real-looking invoice paid.'
}

$clientVersion = $null
$fileWebhookPassword = $null

if ($CredentialFile) {
    if (-not (Test-Path $CredentialFile)) {
        @'
# Paste the values from the PhonePe Business dashboard after each = sign.
# Save the file and close the editor to continue. This file is shredded afterwards.
CLIENT_ID=
CLIENT_SECRET=
CLIENT_VERSION=1
'@ | Set-Content -Path $CredentialFile -Encoding utf8
        Write-Host "Opening $CredentialFile - paste the values, save, then CLOSE the editor." -ForegroundColor Yellow
        Start-Process notepad.exe -ArgumentList $CredentialFile -Wait
    }

    $map = @{}
    foreach ($line in Get-Content $CredentialFile) {
        if ($line -match '^\s*([A-Za-z_]+)\s*=\s*(.+?)\s*$') { $map[$Matches[1].ToUpper()] = $Matches[2] }
    }
    $clientId = $map['CLIENT_ID']
    $clientSecret = $map['CLIENT_SECRET']
    $clientVersion = $map['CLIENT_VERSION']
    $fileWebhookPassword = $map['WEBHOOK_PASSWORD']

    # Overwrite before deleting so the values do not linger in free space.
    $junk = 'x' * 512
    Set-Content -Path $CredentialFile -Value $junk -NoNewline
    Remove-Item $CredentialFile -Force
    Write-Host "Read and shredded $CredentialFile" -ForegroundColor DarkGray
}
else {
    $clientId = Read-Plain 'PhonePe Client ID'
    $clientSecret = Read-Plain 'PhonePe Client Secret'
    $clientVersion = Read-Host -Prompt 'PhonePe Client Version (press Enter for 1)'
}

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
    $webhookPassword = if ($fileWebhookPassword) { $fileWebhookPassword } else { Read-Plain 'PhonePe Webhook Password (the one configured with your POC)' }
}

# Fail before writing anything: a secure prompt that ate a paste yields a 1-character value,
# which would otherwise be stored and only surface later as an opaque 400 from PhonePe.
foreach ($check in @(@{ Name = 'client id'; Value = $clientId }, @{ Name = 'client secret'; Value = $clientSecret })) {
    if ([string]::IsNullOrWhiteSpace($check.Value)) { throw "The $($check.Name) is empty." }
    if ($check.Value.Length -lt 8) {
        throw "The $($check.Name) is only $($check.Value.Length) character(s) - the paste did not register. Re-run with -CredentialFile <path>."
    }
}
if ([string]::IsNullOrWhiteSpace($webhookPassword)) { throw 'The webhook password is empty.' }

Write-Host ("Captured client id ({0} chars) and secret ({1} chars)." -f $clientId.Length, $clientSecret.Length) -ForegroundColor DarkGray

# Values travel on stdin inside a quoted here-doc: nothing lands in argv or shell history.
Send-Remote @"
set -e
cd $($t.Root)
grep -q '^name: $($t.Project)' docker-compose.yml || { echo 'ABORT: wrong compose file in this directory'; exit 1; }
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
$($t.Own)
docker compose up -d --force-recreate smms-api >/dev/null 2>&1
sleep 6

echo "--- as the container sees it (no secrets printed) ---"
echo "  enabled:     `$(docker exec $($t.Api) printenv PhonePe__Enabled)"
echo "  environment: `$(docker exec $($t.Api) printenv PhonePe__Environment)"
echo "  allowlist:   `$(docker exec $($t.Api) printenv PhonePe__AllowedSocieties)"
CID=`$(docker exec $($t.Api) printenv PhonePe__ClientId)
echo "  client id:   `${CID:0:4}... (`${#CID} chars)"
SEC=`$(docker exec $($t.Api) printenv PhonePe__ClientSecret)
echo "  secret:      `${#SEC} chars"
WHP=`$(docker exec $($t.Api) printenv PhonePe__WebhookPassword)
echo "  webhook pwd: `${#WHP} chars"

echo "--- do the credentials actually work? ---"
CVER=`$(docker exec $($t.Api) printenv PhonePe__ClientVersion)
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
BODY='{"event":"checkout.order.completed","payload":{"merchantOrderId":"SMMS_probe_0_x","metaInfo":{"udf1":"$AllowedSocieties"}}}'
echo "  unauthenticated POST -> `$(curl -s -o /dev/null -w '%{http_code}' --resolve $($t.WebhookHost):443:127.0.0.1 -X POST https://$($t.WebhookHost)/api/webhooks/phonepe -H 'Content-Type: application/json' -d "`$BODY")  (401 expected: known society, no valid auth header)"
"@

Clear-Residue

$clientId = $clientSecret = $webhookPassword = $null
[System.GC]::Collect()

Write-Host ''
Write-Host 'Next: enable online payments for the society, then send the webhook URL to your POC.' -ForegroundColor Green
Write-Host "  URL:      https://$($t.WebhookHost)/api/webhooks/phonepe"
Write-Host '  Auth:     SHA'
Write-Host "  Username: $WebhookUsername"
Write-Host '  Events:   checkout.order.completed, checkout.order.failed'

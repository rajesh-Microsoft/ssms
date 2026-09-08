<#
.SYNOPSIS
Promotes an exact commit along local -> dev -> pre-prod -> UAT.

.DESCRIPTION
UAT is the LAST stop because it hosts societies with real money in them. A commit may
only land on an environment once the SAME commit has been deployed to, and signed off
on, the environment before it: dev -> pprod -> uat. The sign-off is recorded on the box
as DEPLOYED_VERIFIED and is cleared by the next deploy there, so it can never outlive
the code it refers to.

dev and UAT share the MCAPS VM and are driven over SSH. Pre-prod lives on a separate
MSDN VM where SSH from this workstation is blocked by local endpoint security, so it is
driven through `az vm run-command`, with the archive staged in blob storage.

.EXAMPLE
  ./deploy/promote.ps1 -Environment dev
  # ...test on dev...
  ./deploy/promote.ps1 -Environment dev -MarkVerified
  ./deploy/promote.ps1 -Environment pprod
  # ...test on pre-prod...
  ./deploy/promote.ps1 -Environment pprod -MarkVerified
  ./deploy/promote.ps1 -Environment uat
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev', 'pprod', 'uat')][string]$Environment,
    [string]$Commit = 'main',
    [switch]$MarkVerified,
    # Forces every box to be driven through the Azure agent instead of SSH. Some workstations
    # have outbound port 22 blocked by endpoint security (github.com:22 works, Azure VMs do not),
    # which is a property of the machine you are sitting at, not of one environment - so this
    # applies to all targets, including the previous one whose sign-off is read.
    [ValidateSet('ssh', 'runcommand')][string]$Transport,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'
$stamp = Get-Date -Format 'yyyyMMddTHHmmssZ'

# Pre-prod: MSDN subscription, its own VM, reached only through the Azure agent.
$pprodSub = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d'
$pprodRg = 'smms-pprod-rg'
$pprodVm = 'smms-pprod-vm'
$pprodSa = 'smmspprod183143'

# dev and UAT share one VM on the MCAPS subscription.
$devSub = '35fafe5f-7621-4ee4-8bae-c2accf4fec38'
$devRg = 'ssms-prod-rg'
$devVm = 'ssms-webserver'
# Staging only needs to be an HTTPS URL the box can curl, so it does not have to live on the
# same subscription as the VM. The MCAPS backup account disables shared keys and its data plane
# rejects tokens from the signed-in tenant, so the pre-prod account carries the archive for
# every target. The blob is private, read-only, SAS-limited to 30 minutes and deleted after.
$stageSub = $pprodSub
$stageRg = $pprodRg
$stageSa = $pprodSa

$targets = @{
    dev   = @{
        Transport = 'ssh'
        Sub       = $devSub
        Rg        = $devRg
        Vm        = $devVm
        Sa        = $stageSa
        SaRg      = $stageRg
        SaSub     = $stageSub
        # Absolute, never "~": run-command executes as root with HOME unset, so a tilde would
        # resolve to /root and the deploy would miss ssmsadmin's tree entirely.
        Root      = '/home/ssmsadmin/smms-dev/SMMS'
        Image     = 'smms-dev-smms-api'
        Api       = 'smms-dev-api'
        Ui        = 'smms-dev-ui'
        Site      = 'http://172.17.0.1:8081/'
        ApiProbe  = 'http://172.17.0.1:8081/api/me'
        CurlOpts  = ''
        # The dev compose file and the nginx conf it mounts are not at the paths the archive
        # ships, so both are put back on every deploy rather than living only on the box.
        Restore   = 'cp deploy/dev-vm/docker-compose.yml docker-compose.yml && cp deploy/dev-vm/nginx.dev-vm.conf nginx/nginx.dev-vm.conf'
        Guard     = "if ! grep -q '^name: smms-dev' docker-compose.yml; then echo 'ABORT: dev compose file missing, refusing to run compose'; exit 1; fi"
        # Extraction under run-command lands as root; hand the tree back or the next SSH deploy
        # cannot write to it. A no-op when the files are already ssmsadmin's.
        Own       = 'chown -R ssmsadmin:ssmsadmin /home/ssmsadmin/smms-dev 2>/dev/null || true'
        Previous  = $null
    }
    pprod = @{
        Transport = 'runcommand'
        Sub       = $pprodSub
        Rg        = $pprodRg
        Vm        = $pprodVm
        Sa        = $pprodSa
        SaRg      = $pprodRg
        SaSub     = $stageSub
        Root      = '/opt/smms/app'
        Image     = 'smms-pprod-smms-api'
        Api       = 'smms-pprod-api'
        Ui        = 'smms-pprod-ui'
        Site      = 'https://pprod-aadya.ssms.yuvaansoft.shop/'
        ApiProbe  = 'https://pprod-aadya.ssms.yuvaansoft.shop/api/health'
        # Pre-prod terminates its own TLS, so probe it by name but resolve it to itself.
        CurlOpts  = '--resolve pprod-aadya.ssms.yuvaansoft.shop:443:127.0.0.1'
        Restore   = 'cp deploy/pprod/docker-compose.yml docker-compose.yml'
        Guard     = "if ! grep -q '^name: smms-pprod' docker-compose.yml; then echo 'ABORT: pre-prod compose file missing, refusing to run compose'; exit 1; fi"
        # run-command runs as root; hand the tree back so the box stays usable as ssmsadmin.
        Own       = 'chown -R ssmsadmin:ssmsadmin /opt/smms'
        Previous  = 'dev'
    }
    uat   = @{
        Transport = 'ssh'
        Sub       = $devSub
        Rg        = $devRg
        Vm        = $devVm
        Sa        = $stageSa
        SaRg      = $stageRg
        SaSub     = $stageSub
        Root      = '/home/ssmsadmin/smms/SMMS'
        Image     = 'smms-smms-api'
        Api       = 'smms-api'
        Ui        = 'smms-ui'
        Site      = 'https://demo.yuvaansoft.shop/'
        ApiProbe  = 'https://demo.yuvaansoft.shop/api/me'
        CurlOpts  = ''
        Restore   = 'true'
        Guard     = "if grep -q '^name:' docker-compose.yml; then echo 'ABORT: a non-UAT compose file landed on UAT'; exit 1; fi`nif ! grep -q 'container_name: smms-api' docker-compose.yml; then echo 'ABORT: unrecognised compose file'; exit 1; fi"
        Own       = 'chown -R ssmsadmin:ssmsadmin /home/ssmsadmin/smms 2>/dev/null || true'
        Previous  = 'pprod'
    }
}

# A blocked-SSH workstation cannot reach ANY box that way, including the previous environment
# whose sign-off gate is read below.
if ($Transport) { foreach ($k in @($targets.Keys)) { $targets[$k].Transport = $Transport } }
$t = $targets[$Environment]

function Get-RunCommandStdout([string]$Raw) {
    try { $json = $Raw | ConvertFrom-Json } catch { return $Raw }
    $msg = ($json.value | ForEach-Object { $_.message }) -join "`n"
    if ($msg -match '(?s)\[stdout\](.*?)(\[stderr\]|$)') { return $Matches[1] }
    return $msg
}

# Every remote script ends by echoing __PROMOTE_OK__. run-command reports "Enable succeeded"
# even when the script inside it failed, so that marker is the only trustworthy signal.
function Invoke-Remote([hashtable]$Target, [string]$Script, [switch]$AllowFail) {
    $body = ($Script -replace "`r", '')
    if ($Target.Transport -eq 'ssh') {
        $out = ($body | ssh -i $key $vm 'bash -s' 2>&1 | Out-String)
        Write-Host $out
        if (-not $AllowFail -and $LASTEXITCODE -ne 0) { throw "remote script failed with exit code $LASTEXITCODE" }
    }
    else {
        $tmp = New-TemporaryFile
        [System.IO.File]::WriteAllText($tmp.FullName, $body)
        $raw = (az vm run-command invoke --subscription $($Target.Sub) -g $($Target.Rg) -n $($Target.Vm) `
                --command-id RunShellScript --scripts "@$($tmp.FullName)" -o json 2>&1 | Out-String)
        Remove-Item $tmp.FullName -ErrorAction SilentlyContinue
        $out = Get-RunCommandStdout $raw
        Write-Host $out
    }
    if (-not $AllowFail -and $out -notmatch '__PROMOTE_OK__') { throw "remote script on $Environment did not run to completion" }
    return $out
}

function Get-RemoteVerified([string]$EnvKey) {
    $prev = $targets[$EnvKey]
    $out = Invoke-Remote $prev "cat $($prev.Root)/DEPLOYED_VERIFIED 2>/dev/null; echo __PROMOTE_OK__" -AllowFail
    # Without the marker the box was never reached, which is not the same as "not signed off".
    if ($out -notmatch '__PROMOTE_OK__') {
        throw "Could not read the $EnvKey sign-off: the box did not answer. If $EnvKey is on the SSH-gated VM, run tools\request-jit.ps1 and retry."
    }
    $match = [regex]::Match($out, '[0-9a-f]{40}')
    if ($match.Success) { return $match.Value }
    return ''
}

$sha = (git rev-parse $Commit).Trim()
$short = $sha.Substring(0, 7)

$nextEnv = @{ dev = 'pprod'; pprod = 'uat' }[$Environment]

# ── Sign-off mode: record that this commit passed testing where it is deployed ──
if ($MarkVerified) {
    Write-Host "Marking $short verified on $Environment" -ForegroundColor Cyan
    $null = Invoke-Remote $t @"
cd $($t.Root)
deployed=`$(cat DEPLOYED_COMMIT 2>/dev/null | tr -d '\r\n')
if [ "`$deployed" != "$sha" ]; then
  echo "ABORT: $Environment is running `$deployed, not $sha"
  exit 1
fi
echo "$sha" > DEPLOYED_VERIFIED
echo "$Environment signed off at:"
cat DEPLOYED_VERIFIED
echo __PROMOTE_OK__
"@
    Write-Host "`n$Environment verified at $short." -ForegroundColor Green
    if ($nextEnv) { Write-Host "Promotion to $nextEnv at this commit is now unlocked." }
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

if ($t.Previous -and -not $Force) {
    Write-Host "Checking the $($t.Previous) sign-off..."
    $verified = Get-RemoteVerified $t.Previous
    if ($verified -ne $sha) {
        $state = if ($verified) { "$($t.Previous) is signed off at $($verified.Substring(0,7))" } else { "$($t.Previous) has no sign-off recorded" }
        throw @"
Refusing to promote to ${Environment}: $state, not $short.
The same commit must pass $($t.Previous) first. Deploy it there, test it, then run:
  ./deploy/promote.ps1 -Environment $($t.Previous) -MarkVerified
"@
    }
    Write-Host "$($t.Previous) is signed off at $short." -ForegroundColor Green
}

# ── Ship ──
$tgz = Join-Path $env:TEMP "promote-$short.tgz"
git archive --format=tar.gz -o $tgz $sha
Write-Host ("archive: {0:N1} MB" -f ((Get-Item $tgz).Length / 1MB))

$blob = "promote-$short.tgz"
$saKey = $null
if ($t.Transport -eq 'ssh') {
    scp -i $key $tgz "${vm}:/tmp/promote.tgz"
    if ($LASTEXITCODE -ne 0) { Remove-Item $tgz -ErrorAction SilentlyContinue; throw 'scp failed' }
    $fetch = 'test -f /tmp/promote.tgz'
}
else {
    # No scp to this box, so the archive travels through a private container and a
    # short-lived read-only SAS that only ever exists inside the script handed to the agent.
    $saKey = az storage account keys list -n $($t.Sa) -g $($t.SaRg) --subscription $($t.SaSub) --query '[0].value' -o tsv
    az storage container create -n stage --account-name $($t.Sa) --account-key $saKey --only-show-errors -o none
    az storage blob upload --account-name $($t.Sa) --account-key $saKey -c stage -n $blob -f $tgz --overwrite --only-show-errors -o none
    $expiry = (Get-Date).ToUniversalTime().AddMinutes(30).ToString('yyyy-MM-ddTHH:mmZ')
    $sas = (az storage blob generate-sas --account-name $($t.Sa) --account-key $saKey -c stage -n $blob --permissions r --expiry $expiry --https-only -o tsv).Trim()
    $fetch = "curl -fsSL 'https://$($t.Sa).blob.core.windows.net/stage/$blob`?$sas' -o /tmp/promote.tgz"
}
Remove-Item $tgz

$null = Invoke-Remote $t @"
set -e
cd $($t.Root)

echo "===== ROLLBACK IMAGE ====="
docker tag $($t.Image) $($t.Image):rollback-$stamp || true
echo "rollback tag: $($t.Image):rollback-$stamp"

echo ""
echo "===== EXTRACTING $short ====="
# The local-nuget blobs refuse to be clobbered in place and are byte identical anyway.
$fetch
rm -f api/SMMS.Api/local-nuget/*.nupkg
tar xzf /tmp/promote.tgz -C $($t.Root) --overwrite
rm -f /tmp/promote.tgz

# The archive always carries the PRODUCTION compose file at the repo root. On dev and
# pre-prod it must be swapped back before any compose command runs, or the project name
# falls back to the directory name and compose recreates another stack's containers.
# Extraction is deliberately in place: .env is generated per box and is not in the archive.
$($t.Restore)
$($t.Guard)
echo -n "compose project: " ; grep -m1 '^name:' docker-compose.yml || echo "(none, defaults to directory)"

echo "$sha" > DEPLOYED_COMMIT
rm -f DEPLOYED_VERIFIED
# Append-only record of what landed here. A box otherwise remembers only the commit it is
# running, so without this there is no deployment history to read back.
# The date subexpression is backtick-escaped so it runs on the box, not here.
echo "$sha|`$(date -u +%Y-%m-%dT%H:%M:%SZ)|$env:USERNAME|$Environment" >> DEPLOYED_HISTORY
$($t.Own)
echo -n "DEPLOYED_COMMIT: " ; cat DEPLOYED_COMMIT

echo ""
echo "===== RAZORPAY ALLOWLIST (unchanged by deploys) ====="
grep '^RAZORPAY_ALLOWED_SOCIETIES=' .env 2>/dev/null || echo "(none)"
echo "--- end allowlist ---"

echo ""
echo "===== BUILD ====="
docker compose build smms-api > /tmp/promote-build.log 2>&1 || { echo "BUILD FAILED"; tail -n 30 /tmp/promote-build.log; exit 1; }
tail -n 6 /tmp/promote-build.log
echo "--- end build ---"

echo ""
echo "===== RECREATE API (applies pending migrations to every tenant) ====="
docker compose up -d --force-recreate smms-api > /tmp/promote-up.log 2>&1 || { echo "UP FAILED"; cat /tmp/promote-up.log; exit 1; }
cat /tmp/promote-up.log
# The UI is bind-mounted, but its nginx conf ships in the archive and needs a reload.
docker exec $($t.Ui) nginx -s reload 2>/dev/null || true

echo ""
echo "===== HEALTH ====="
# The site root is static content and answers 200 even when the API is down. Probing an
# /api route is tenant-sensitive (an unknown Host 404s before routing), so liveness is
# judged on whether the API answered at all: a gateway code means it is not accepting
# connections, anything else means the process is up and serving.
api=000
for i in 1 2 3 4 5 6 7 8 9 10; do
  api=`$(curl -s $($t.CurlOpts) -o /dev/null -w '%{http_code}' $($t.ApiProbe) || true)
  case "`$api" in 000|502|503|504) sleep 6;; *) break;; esac
done
site=`$(curl -s $($t.CurlOpts) -o /dev/null -w '%{http_code}' $($t.Site) || true)
echo "site -> `$site"
echo "api  -> `$api  (any non-gateway code means the API answered)"
echo -n "tenants migrated: "
docker logs $($t.Api) 2>&1 | grep -c 'Database is ready' || true
echo "exceptions:"
docker logs $($t.Api) 2>&1 | grep -iE 'unhandled|exception|fail: ' | head -n 5 || true
echo "(nothing above means clean)"
case "`$api" in 000|502|503|504) echo "ABORT: API not answering"; exit 1;; esac
docker ps --filter "name=$($t.Api)" --format '{{.Names}}  {{.Status}}'
echo __PROMOTE_OK__
"@

if ($saKey) {
    az storage blob delete --account-name $($t.Sa) --account-key $saKey -c stage -n $blob --only-show-errors -o none
    Write-Host "staged archive deleted from blob storage"
}

Write-Host "`n$short is live on $Environment." -ForegroundColor Green
Write-Host "Roll back with:  docker tag $($t.Image):rollback-$stamp $($t.Image) && docker compose up -d --force-recreate smms-api"

# The hosted portal cannot reach pre-prod, so refresh its mirror here. Best-effort: a
# dashboard that is briefly out of date must never fail a deployment that has succeeded.
if ($Environment -eq 'pprod') {
    try { & (Join-Path $PSScriptRoot 'pprod/mirror-state.ps1') -Transport ($Transport ? $Transport : 'ssh') }
    catch { Write-Warning "portal mirror not updated: $_. Run deploy/pprod/mirror-state.ps1 once the JIT window is open." }
}

if ($nextEnv) {
    Write-Host "Test it, then sign off:  ./deploy/promote.ps1 -Environment $Environment -MarkVerified"
    Write-Host "Then promote onward:    ./deploy/promote.ps1 -Environment $nextEnv"
}

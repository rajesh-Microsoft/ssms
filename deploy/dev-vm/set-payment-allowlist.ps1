<#
.SYNOPSIS
Sets RAZORPAY_ALLOWED_SOCIETIES on dev or UAT and restarts that stack's API.

.DESCRIPTION
The allowlist is the outer gate on online payments; each society's own toggle is the inner
one. Widening the allowlist exposes every listed society whose toggle is already ON, so the
current toggles are printed before anything changes. Turn a society OFF first if you are
adding it only to test.

.EXAMPLE
  ./deploy/dev-vm/set-payment-allowlist.ps1 -Environment uat -Societies demo,aadya
  ./deploy/dev-vm/set-payment-allowlist.ps1 -Environment uat -Societies demo,aadya -Apply
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev', 'uat')][string]$Environment,
    [Parameter(Mandatory)][string[]]$Societies,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$cfg = @{
    dev = @{ Root = '~/smms-dev/SMMS'; Api = 'smms-dev-api'; Sql = 'smms-dev-sql' }
    uat = @{ Root = '~/smms/SMMS'; Api = 'smms-api'; Sql = 'sqlserver' }
}[$Environment]

$list = ($Societies -join ',').Trim()

$remote = @"
set -e
cd $($cfg.Root)

PW=`$(docker exec $($cfg.Api) printenv ControlPlane__TenantConnectionTemplate | sed -n "s/.*Password=\([^;]*\).*/\1/p" | tr -d "\r\n")

echo "===== CURRENT ====="
grep "^RAZORPAY_ALLOWED_SOCIETIES=" .env || echo "(not set)"
echo ""
echo "per-society online payment toggles:"
docker exec $($cfg.Sql) /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "`$PW" -C -d SmmsControlDb \
  -Q "SET NOCOUNT ON; SELECT [Key], DbName FROM Societies WHERE Status = 'Active';" -h -1 -W -s "|" \
  | while IFS='|' read -r k db; do
      [ -z "`$k" ] && continue
      t=`$(docker exec $($cfg.Sql) /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "`$PW" -C -d "`$db" \
            -Q "SET NOCOUNT ON; SELECT CAST(OnlinePaymentsEnabled AS int) FROM Settings;" -h -1 -W 2>/dev/null | tr -d " \r")
      echo "  `$k = `${t:-?}"
    done
echo "--- end current ---"
"@

if ($Apply) {
    $remote += @"

echo ""
echo "===== APPLYING $list ====="
cp .env .env.bak
sed -i 's/^RAZORPAY_ALLOWED_SOCIETIES=.*/RAZORPAY_ALLOWED_SOCIETIES=$list/' .env
grep "^RAZORPAY_ALLOWED_SOCIETIES=" .env

echo ""
echo "===== RESTARTING $($cfg.Api) ====="
docker compose up -d --force-recreate smms-api > /tmp/allowlist.log 2>&1 ; echo "exit: `$?"
tail -n 5 /tmp/allowlist.log
"@
}
$remote += "`necho `"--- done ---`""

($remote -replace "`r", '') | ssh -i $key $vm 'bash -s'

if (-not $Apply) {
    Write-Host "`nDry run. Re-run with -Apply to set the allowlist to: $list" -ForegroundColor Yellow
}

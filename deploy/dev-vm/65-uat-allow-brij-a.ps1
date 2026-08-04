$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Order matters: brij-a's per-society toggle is ON, so widening the allowlist first would
# expose real dues to test-key payments for the length of the restart. Close gate 2 first.
$remote = @'
set -e
cd ~/smms/SMMS

PW=$(docker exec smms-api printenv ControlPlane__TenantConnectionTemplate | sed -n "s/.*Password=\([^;]*\).*/\1/p" | tr -d "\r\n")

echo "===== STEP 1: switch brij-a OFF before it becomes reachable ====="
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d "SmmsDb_Brij-a" -Q "UPDATE Settings SET OnlinePaymentsEnabled = 0;" -W
echo -n "brij-a toggle now: "
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d "SmmsDb_Brij-a" -Q "SET NOCOUNT ON; SELECT CAST(OnlinePaymentsEnabled AS int) FROM Settings;" -h -1 -W

echo ""
echo "===== STEP 2: add brij-a to the allowlist ====="
cp .env .env.bak
sed -i 's/^RAZORPAY_ALLOWED_SOCIETIES=.*/RAZORPAY_ALLOWED_SOCIETIES=demo,aadya,brij-a/' .env
grep "^RAZORPAY_ALLOWED_SOCIETIES=" .env ; echo "--- end env ---"

echo ""
echo "===== STEP 3: recreate the UAT api from its OWN directory ====="
docker compose up -d --force-recreate smms-api > /tmp/brij.log 2>&1 ; echo "exit: $?"
tail -n 5 /tmp/brij.log ; echo "--- end up ---"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"
Write-Host "--- done ---"

$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
PRD_SA=$(docker exec sqlserver printenv SA_PASSWORD | tr -d '\r\n')

echo "===== REAL ACTIVITY ON THE 'PROD' BOX ====="
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -W -d SmmsDb_Aadya -Q "SET NOCOUNT ON; SELECT COUNT(*) Collections, SUM(CASE WHEN Status='Paid' THEN 1 ELSE 0 END) Paid, MAX(PaymentDate) LastPayment FROM Collections;"
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -W -d SmmsDb_Aadya -Q "SET NOCOUNT ON; SELECT COUNT(*) Proofs, MAX(SubmittedAt) LastProof FROM PaymentProofs;"

echo "===== AARADYA ====="
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -W -d SmmsDb_Aaradya -Q "SET NOCOUNT ON; SELECT COUNT(*) Collections, MAX(PaymentDate) LastPayment FROM Collections;"

echo "===== WHY DID IT REBOOT? ====="
journalctl --list-boots 2>/dev/null | tail -n 5
echo "--- shutdown/maintenance messages ---"
journalctl -b -1 -n 15 --no-pager 2>/dev/null | tail -n 15
echo "(if empty, previous boot logs are not persisted)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

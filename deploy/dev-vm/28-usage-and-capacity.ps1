$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
PRD_SA=$(docker exec sqlserver printenv SA_PASSWORD)

echo "===== IS THERE REAL RECENT ACTIVITY ON 'PROD'? (Aadya) ====="
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -W -d SmmsDb_Aadya -Q "SET NOCOUNT ON; SELECT COUNT(*) AS TotalCollections, SUM(CASE WHEN Status='Paid' THEN 1 ELSE 0 END) AS Paid, MAX(PaymentDate) AS LastPayment, MAX(CreatedOn) AS LastCreated FROM Collections;"

echo "===== PAYMENT PROOFS (real members uploading?) ====="
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -W -d SmmsDb_Aadya -Q "SET NOCOUNT ON; SELECT COUNT(*) AS Proofs, MAX(SubmittedAt) AS LastProof FROM PaymentProofs;"

echo "===== AARADYA ====="
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -W -d SmmsDb_Aaradya -Q "SET NOCOUNT ON; SELECT COUNT(*) AS TotalCollections, MAX(PaymentDate) AS LastPayment FROM Collections;"

echo "===== VM CAPACITY (can it host a 3rd stack?) ====="
df -h / | tail -n 1
free -m | head -n 2
echo "docker disk usage:"
docker system df
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

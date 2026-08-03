$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV_SA=$(grep '^DEV_SA_PASSWORD=' ~/smms-dev/SMMS/.env | cut -d= -f2-)
echo "===== BASELINE: invoices for flat A-1004 (before payment) ====="
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -W -d SmmsDb_Demo -Q "SET NOCOUNT ON; SELECT Id, InvoiceNumber, Amount, AmountPaid, Status, PaymentMode, PaymentDate FROM Collections WHERE Id IN (4482,4483,4486,4488) ORDER BY Id;"

echo ""
echo "===== BASELINE: webhook hits in dev API log so far ====="
docker logs smms-dev-api 2>&1 | grep -ciE "webhook|razorpay"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

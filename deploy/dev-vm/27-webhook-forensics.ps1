$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV_SA=$(grep '^DEV_SA_PASSWORD=' ~/smms-dev/SMMS/.env | cut -d= -f2-)

echo "===== WEBHOOK / RAZORPAY LINES IN DEV API LOG ====="
docker logs smms-dev-api 2>&1 | grep -iE "webhook|razorpay" | tail -n 20

echo ""
echo "===== ALL RAZORPAY-PAID INVOICES IN DEMO (who closed them?) ====="
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -W -d SmmsDb_Demo -Q "SET NOCOUNT ON; SELECT Id, InvoiceNumber, Status, PaymentMode, PaymentDate, Remarks FROM Collections WHERE PaymentMode='Razorpay' ORDER BY PaymentDate DESC;"

echo ""
echo "===== CLEAN FULLY-UNPAID INVOICES AVAILABLE TO TEST WITH ====="
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -W -d SmmsDb_Demo -Q "SET NOCOUNT ON; SELECT TOP 8 c.Id, c.InvoiceNumber, c.Amount, c.Status, m.FlatNumber FROM Collections c JOIN Members m ON m.Id=c.MemberId WHERE c.Status IN ('Unpaid','Overdue') AND ISNULL(c.AmountPaid,0)=0 ORDER BY c.Id;"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

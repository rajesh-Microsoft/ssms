$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
PW='YourStrong@Passw0rd'
Q() { docker exec sqlserver $SQLCMD -S localhost -U sa -P "$PW" -C -d SmmsDb_Demo -h -1 -W -s "|" -Q "SET NOCOUNT ON; $1"; }

echo "===== RAZORPAY PAYMENT ATTEMPTS ====="
Q "SELECT Id, CollectionId, Amount, Status, GatewayReference, ReviewRemarks, SubmittedAt FROM PaymentProofs WHERE GatewayName = 'Razorpay' ORDER BY Id DESC;"
echo "--- end ---"

echo ""
echo "===== COLLECTIONS TOUCHED BY THOSE ATTEMPTS ====="
Q "SELECT c.Id, c.Month, c.Year, c.Amount, c.AmountPaid, c.Status, c.PaymentDate FROM Collections c WHERE c.Id IN (SELECT CollectionId FROM PaymentProofs WHERE GatewayName = 'Razorpay');"
echo "--- end ---"

echo ""
echo "===== WEBHOOK ACTIVITY IN LOGS ====="
docker logs smms-api 2>&1 | grep -iE "razorpay" | tail -n 25
echo "--- end ---"

echo ""
echo "===== ANY SIGNATURE OR VERIFY FAILURES ====="
docker logs smms-api 2>&1 | grep -iE "signature verification failed|incomplete payment|no webhook secret" | tail -n 10
echo "(nothing above = no failures)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm  = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV_SA=$(grep '^DEV_SA_PASSWORD=' ~/smms-dev/SMMS/.env | cut -d= -f2-)

echo "===== SET UPI ON DEV demo (dev database only) ====="
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -b -d SmmsDb_Demo -Q "
SET NOCOUNT ON;
UPDATE Settings SET
  UpiId = 'demosociety@ybl',
  UpiPayeeName = 'Demo Society (Sandbox)',
  BankName = 'Demo Bank',
  BankAccountName = 'Demo Society Sandbox',
  BankAccountNumber = '000111222333',
  BankIfsc = 'DEMO0000123';
SELECT 'rows updated: ' + CAST(@@ROWCOUNT AS varchar);"

echo ""
echo "===== VERIFY payment-options AS A MEMBER ====="
DEV='http://172.17.0.1:8081'
DH='Host: demo.yuvaansoft.shop'
TOK=$(curl -s -X POST "$DEV/api/auth/login" -H "$DH" -H 'Content-Type: application/json' \
      -d '{"username":"res-a1004","password":"demo123"}' \
      | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
curl -s "$DEV/api/member/payment-options" -H "$DH" -H "Authorization: Bearer $TOK"
echo ""

echo ""
echo "===== PRODUCTION demo UNCHANGED (must still be null) ====="
PRD_SA=$(docker exec sqlserver printenv SA_PASSWORD)
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -h -1 -W -d SmmsDb_Demo \
  -Q "SET NOCOUNT ON; SELECT 'prod demo UpiId=[' + ISNULL(UpiId,'<null>') + ']' FROM Settings;"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

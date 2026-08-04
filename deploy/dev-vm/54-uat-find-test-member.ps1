$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
PW='YourStrong@Passw0rd'

echo "===== UAT DATABASES ====="
docker exec sqlserver $SQLCMD -S localhost -U sa -P "$PW" -C -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'SmmsDb%' ORDER BY name;"
echo "--- end ---"

echo ""
echo "===== DEMO SOCIETY MEMBERS (UAT) ====="
docker exec sqlserver $SQLCMD -S localhost -U sa -P "$PW" -C -d SmmsDb_Demo -h -1 -W -s "|" -Q "SET NOCOUNT ON; SELECT TOP 8 Username, Role, LEN(PasswordHash) FROM Users ORDER BY Id;"
echo "--- end ---"

echo ""
echo "===== UNPAID COLLECTIONS AVAILABLE TO PAY (UAT demo) ====="
docker exec sqlserver $SQLCMD -S localhost -U sa -P "$PW" -C -d SmmsDb_Demo -h -1 -W -s "|" -Q "SET NOCOUNT ON; SELECT TOP 5 Id, FlatNumber, Month, Year, Amount, Status FROM Collections WHERE Status <> 'Paid' ORDER BY Id DESC;"
echo "--- end ---"

echo ""
echo "===== DEV res-a1004 HASH LENGTH (source of the known demo123 hash) ====="
docker exec smms-dev-sql $SQLCMD -S localhost -U sa -P "$PW" -C -d SmmsDb_Demo -h -1 -W -s "|" -Q "SET NOCOUNT ON; SELECT Username, Role, LEN(PasswordHash) FROM Users WHERE Username = 'res-a1004';"
echo "--- end ---"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

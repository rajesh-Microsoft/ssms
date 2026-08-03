$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV_SA=$(grep '^DEV_SA_PASSWORD=' ~/smms-dev/SMMS/.env | cut -d= -f2-)
PRD_SA=$(docker exec sqlserver printenv SA_PASSWORD)
SQL="SET NOCOUNT ON; SELECT DB_NAME() + '  UpiId=[' + ISNULL(UpiId,'<null>') + ']  Payee=[' + ISNULL(UpiPayeeName,'<null>') + ']  Bank=[' + ISNULL(BankName,'<null>') + ']' FROM Settings;"

echo "===== DEV ====="
for db in SmmsDb_Demo SmmsDb_Aadya SmmsDb_Aaradya; do
  docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W -d "$db" -Q "$SQL"
done

echo ""
echo "===== PRODUCTION (read-only) ====="
for db in SmmsDb_Demo SmmsDb_Aadya SmmsDb_Aaradya; do
  docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -h -1 -W -d "$db" -Q "$SQL"
done
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

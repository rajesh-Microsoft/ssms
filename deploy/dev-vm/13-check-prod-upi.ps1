$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
PRD_SA=$(docker exec sqlserver printenv SA_PASSWORD)
SQL="SET NOCOUNT ON; SELECT DB_NAME() + '  UpiId=[' + ISNULL(UpiId,'<null>') + ']  Payee=[' + ISNULL(UpiPayeeName,'<null>') + ']' FROM Settings;"
echo "===== PRODUCTION demo (read-only) ====="
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -h -1 -W -d SmmsDb_Demo -Q "$SQL"
echo "===== PRODUCTION aadya (read-only) ====="
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -h -1 -W -d SmmsDb_Aadya -Q "$SQL"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

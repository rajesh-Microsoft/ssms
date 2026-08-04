$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== which SA env vars exist on the prod sqlserver container ====="
docker exec sqlserver printenv | grep -iE 'SA_PASSWORD|MSSQL' | sed 's/=.*/=<redacted>/'
echo ""
echo "captured length via SA_PASSWORD  : $(docker exec sqlserver printenv SA_PASSWORD | wc -c)"
echo "captured length via MSSQL_SA_PASSWORD: $(docker exec sqlserver printenv MSSQL_SA_PASSWORD | wc -c)"
echo ""
echo "===== container uptime (was it recreated?) ====="
docker ps --filter name=sqlserver --format 'table {{.Names}}\t{{.Status}}'
echo ""
echo "===== try a trivial query with the captured value ====="
PRD_SA=$(docker exec sqlserver printenv SA_PASSWORD | tr -d '\r\n')
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PRD_SA" -C -W -Q "SET NOCOUNT ON; SELECT 'login ok';"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

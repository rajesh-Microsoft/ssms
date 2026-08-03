$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV_SA=$(grep '^DEV_SA_PASSWORD=' ~/smms-dev/SMMS/.env | cut -d= -f2-)
q() { docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W -d "$1" -Q "SET NOCOUNT ON; $2"; }

echo "===== Collections COLUMNS ====="
q SmmsDb_Demo "SELECT STRING_AGG(name, ', ') WITHIN GROUP (ORDER BY column_id) FROM sys.columns WHERE object_id = OBJECT_ID('Collections');"

echo ""
echo "===== USERS / MEMBERS PER TENANT ====="
for db in SmmsDb_Demo SmmsDb_Aadya SmmsDb_Aaradya SmmsDb_Sunrise SmmsDb_Lake-view SmmsDb_Vr SmmsDb_Brij-a; do
  printf '%-20s ' "$db"
  q "$db" "SELECT 'users=' + CAST((SELECT COUNT(*) FROM Users) AS varchar) + '  members=' + CAST((SELECT COUNT(*) FROM Members) AS varchar) + '  unpaid=' + CAST((SELECT COUNT(*) FROM Collections WHERE Status <> 'Paid') AS varchar);"
done
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

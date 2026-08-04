$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV_SA=$(grep '^DEV_SA_PASSWORD=' ~/smms-dev/SMMS/.env | cut -d= -f2-)
SQL='SELECT DB_NAME() + N"  users=" + CAST((SELECT COUNT(*) FROM Users) AS nvarchar) + N"  members=" + CAST((SELECT COUNT(*) FROM Members) AS nvarchar) + N"  unpaid=" + CAST((SELECT COUNT(*) FROM Collections WHERE Status <> N"Paid") AS nvarchar);'
SQL=$(echo "$SQL" | tr '"' "'")

for db in SmmsDb_Demo SmmsDb_Aadya SmmsDb_Aaradya SmmsDb_Sunrise SmmsDb_Lake-view SmmsDb_Vr SmmsDb_Brij-a
do
  docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W -d "$db" -Q "SET NOCOUNT ON; $SQL"
done

echo ""
echo "===== DEMO: SAMPLE UNPAID INVOICES ====="
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W -d SmmsDb_Demo -Q "SET NOCOUNT ON; SELECT TOP 5 'id=' + CAST(c.Id AS varchar) + ' flat=' + m.Flat + ' ' + CAST(c.Month AS varchar) + '/' + CAST(c.Year AS varchar) + ' amount=' + CAST(c.Amount AS varchar) + ' status=' + c.Status FROM Collections c JOIN Members m ON m.Id = c.MemberId WHERE c.Status <> 'Paid' ORDER BY c.Id DESC;"

echo ""
echo "===== AADYA: MEMBER LOGINS (usernames only) ====="
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W -d SmmsDb_Aadya -Q "SET NOCOUNT ON; SELECT TOP 6 Username + '  role=' + Role + '  flat=' + ISNULL(Flat,'-') FROM Users ORDER BY Role;"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
H='Host: admin.yuvaansoft.shop'
B='http://172.17.0.1:8081'

SUPER=$(docker exec smms-api printenv ControlPlane__SuperAdmin__Password)
TOK=$(curl -s -X POST "$B/api/platform/auth/login" -H "$H" -H 'Content-Type: application/json' \
      -d "{\"username\":\"superadmin\",\"password\":\"$SUPER\"}" \
      | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
[ -z "$TOK" ] && { echo "login failed"; exit 1; }
echo "authenticated"

echo "===== SEED DEMO SOCIETY (dev only) ====="
code=$(curl -s -o /tmp/seed.json -w '%{http_code}' -X POST \
  "$B/api/platform/societies/demo/generate-demo?preset=small" \
  -H "$H" -H "Authorization: Bearer $TOK")
echo "HTTP $code"
head -c 400 /tmp/seed.json; echo ""
rm -f /tmp/seed.json

echo ""
echo "===== DEMO CONTENTS AFTER SEED ====="
DEV_SA=$(grep '^DEV_SA_PASSWORD=' ~/smms-dev/SMMS/.env | cut -d= -f2-)
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W -d SmmsDb_Demo \
  -Q "SET NOCOUNT ON; SELECT 'users=' + CAST((SELECT COUNT(*) FROM Users) AS varchar) + ' members=' + CAST((SELECT COUNT(*) FROM Members) AS varchar) + ' unpaid=' + CAST((SELECT COUNT(*) FROM Collections WHERE Status <> 'Paid') AS varchar);"

echo "--- sample member logins ---"
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W -d SmmsDb_Demo \
  -Q "SET NOCOUNT ON; SELECT TOP 5 Username + '  flat=' + ISNULL(Flat,'-') FROM Users WHERE Role = 'Member' ORDER BY Username;"

echo "--- sample unpaid invoices ---"
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W -d SmmsDb_Demo \
  -Q "SET NOCOUNT ON; SELECT TOP 5 'id=' + CAST(c.Id AS varchar) + ' flat=' + m.Flat + ' ' + CAST(c.Month AS varchar) + '/' + CAST(c.Year AS varchar) + ' amount=' + CAST(c.Amount AS varchar) + ' ' + c.Status FROM Collections c JOIN Members m ON m.Id = c.MemberId WHERE c.Status <> 'Paid' ORDER BY c.Id DESC;"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

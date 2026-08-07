$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV='http://172.17.0.1:8081'
DH='Host: demo.yuvaansoft.shop'

echo "===== caretaker page served ====="
curl -s -o /dev/null -w '  /caretaker/ -> %{http_code}\n' "$DEV/caretaker/" -H "$DH"
curl -s "$DEV/caretaker/" -H "$DH" | grep -c 'Sign in to start the day' || true

echo "===== endpoints exist and are protected ====="
curl -s -o /dev/null -w '  anonymous /api/caretaker/summary -> %{http_code} (expect 401)\n' "$DEV/api/caretaker/summary" -H "$DH"

echo "===== a resident must not reach the gate app ====="
TOK=$(curl -s -X POST "$DEV/api/auth/login" -H "$DH" -H 'Content-Type: application/json' \
      -d '{"username":"res-a1004","password":"demo123"}' \
      | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
[ -z "$TOK" ] && echo "  member login FAILED" || echo "  member login OK"
curl -s -o /dev/null -w '  member /api/caretaker/summary -> %{http_code} (expect 403)\n' \
  "$DEV/api/caretaker/summary" -H "$DH" -H "Authorization: Bearer $TOK"
curl -s -o /dev/null -w '  member /api/caretaker/visitors -> %{http_code} (expect 403)\n' \
  "$DEV/api/caretaker/visitors" -H "$DH" -H "Authorization: Bearer $TOK"

echo "===== tables created on every dev tenant ====="
PW=$(docker exec smms-dev-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p' | tr -d '\r\n')
for DB in SmmsDb_Demo SmmsDb_Aadya; do
  N=$(docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d $DB -h -1 -W \
      -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables WHERE name IN ('Visitors','Deliveries','DailyChecklists','DailyChecklistItems');" 2>/dev/null | tr -d '\r ')
  C=$(docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d $DB -h -1 -W \
      -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('Complaints') AND name IN ('PhotoPath','PhotoContentType');" 2>/dev/null | tr -d '\r ')
  echo "  $DB caretaker tables=$N/4 complaint photo columns=$C/2"
done

echo "===== uploads volume mounted ====="
docker inspect smms-dev-api --format '{{range .Mounts}}{{.Name}} -> {{.Destination}}{{"\n"}}{{end}}' | grep uploads || echo "  NO uploads mount"

echo "--- done ---"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

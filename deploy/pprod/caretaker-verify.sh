#!/bin/bash
# Read-only verification of the caretaker feature on pre-prod. Creates no data.
cd /opt/smms/app
BASE='https://pprod-aadya.ssms.yuvaansoft.shop'
R="--resolve pprod-aadya.ssms.yuvaansoft.shop:443:127.0.0.1"

echo "===== caretaker page ====="
curl -s $R -o /dev/null -w '  /caretaker/ -> %{http_code}\n' "$BASE/caretaker/"
curl -s $R "$BASE/caretaker/" | grep -c 'Sign in to start the day' || true

echo "===== endpoint protected ====="
curl -s $R -o /dev/null -w '  anonymous /api/caretaker/summary -> %{http_code} (expect 401)\n' "$BASE/api/caretaker/summary"

echo "===== admin role picker shipped ====="
curl -s $R "$BASE/index.html" | grep -c '<option>Caretaker</option>' || true

echo "===== schema on every pre-prod tenant ====="
PW=$(docker exec smms-pprod-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p' | tr -d '\r\n')
DBS=$(docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W \
      -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'SmmsDb[_]%';" 2>/dev/null | tr -d '\r')
for DB in $DBS; do
  N=$(docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d $DB -h -1 -W \
      -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables WHERE name IN ('Visitors','Deliveries','DailyChecklists','DailyChecklistItems');" 2>/dev/null | tr -d '\r ')
  C=$(docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d $DB -h -1 -W \
      -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('Complaints') AND name IN ('PhotoPath','PhotoContentType');" 2>/dev/null | tr -d '\r ')
  echo "  $DB tables=$N/4 photoCols=$C/2"
done

echo "===== existing data untouched ====="
docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d SmmsDb_Aadya -h -1 -W -s '|' \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Members; SELECT COUNT(*) FROM Collections; SELECT COUNT(*) FROM Complaints;" 2>/dev/null | tr -d '\r'

echo "===== uploads volume ====="
docker inspect smms-pprod-api --format '{{range .Mounts}}{{.Name}} -> {{.Destination}}{{"\n"}}{{end}}' | grep uploads || echo "  NO uploads mount"

echo "__PROMOTE_OK__"

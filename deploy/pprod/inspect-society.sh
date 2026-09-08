#!/bin/bash
# Read-only. Reports one society's figures on whichever box it is run on, so the two sides
# can be compared before deciding to refresh. Detects pre-prod vs UAT from the containers.
set -uo pipefail
SOC='__SOCIETY__'

if docker ps --format '{{.Names}}' | grep -qx smms-pprod-sql; then
  BOX=pre-prod; SQLC=smms-pprod-sql; APIC=smms-pprod-api
  PW=$(grep '^PPROD_SA_PASSWORD=' /opt/smms/app/.env | cut -d= -f2-)
else
  BOX=source; SQLC=sqlserver; APIC=smms-api
  PW=$(docker exec smms-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p' | tr -d '\r\n')
fi

SQLCMD=/opt/mssql-tools18/bin/sqlcmd
docker exec $SQLC ls $SQLCMD >/dev/null 2>&1 || SQLCMD=/opt/mssql-tools/bin/sqlcmd
sq() { docker exec $SQLC $SQLCMD -S localhost -U sa -P "$PW" -C -b -h -1 -W -s '|' -d "$1" -Q "SET NOCOUNT ON; $2"; }

DB=$(sq SmmsControlDb "SELECT DbName FROM Societies WHERE [Key]='$SOC';" | tr -d ' \r\n')
if [ -z "$DB" ]; then echo "$BOX: society '$SOC' not registered"; exit 0; fi

echo "box=$BOX society=$SOC db=$DB"
sq "$DB" "SELECT 'Members', COUNT(*) FROM Members UNION ALL SELECT 'Users', COUNT(*) FROM Users UNION ALL SELECT 'Collections', COUNT(*) FROM Collections UNION ALL SELECT 'CollAmt', ISNULL(SUM(Amount),0) FROM Collections UNION ALL SELECT 'Expenses', COUNT(*) FROM Expenses WHERE IsDeleted=0 UNION ALL SELECT 'ExpAmt', ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0 UNION ALL SELECT 'Liabs', COUNT(*) FROM SocietyLiabilities WHERE IsDeleted=0;"
sq "$DB" "SELECT TOP 1 'Migration', MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;"
echo "uploads: $(docker exec $APIC sh -c 'find /app/uploads -type f | wc -l') file(s)"
df -h / | tail -n 1

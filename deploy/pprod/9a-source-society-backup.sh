#!/bin/bash
# Runs on the SOURCE VM (UAT). Backs up ONE society's database plus /app/uploads and
# uploads a single tarball. Read-only with respect to application data: it only issues
# BACKUP DATABASE, which never modifies the source.
set -uo pipefail
SOC='__SOCIETY__'
APP=/home/ssmsadmin/smms/SMMS

# A .bak on a full disk is how the last deploy died. Refuse rather than half-write one.
AVAIL=$(df -Pk / | awk 'NR==2{print $4}')
echo "free on source: $((AVAIL/1024)) MB"
if [ "$AVAIL" -lt 2097152 ]; then
  echo "ABORT: less than 2 GB free on the source VM. Reclaim space before refreshing."
  exit 1
fi

PW=$(docker exec smms-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p' | tr -d '\r\n')
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
docker exec sqlserver ls $SQLCMD >/dev/null 2>&1 || SQLCMD=/opt/mssql-tools/bin/sqlcmd
sq() { docker exec sqlserver $SQLCMD -S localhost -U sa -P "$PW" -C -b -h -1 -W -s '|' -d "$1" -Q "SET NOCOUNT ON; $2"; }

# Never hardcode the database name: the control plane owns the society -> DbName mapping.
DB=$(sq SmmsControlDb "SELECT DbName FROM Societies WHERE [Key]='$SOC';" | tr -d ' \r\n')
if [ -z "$DB" ]; then echo "ABORT: society '$SOC' has no row in SmmsControlDb.Societies"; exit 1; fi
echo "society=$SOC db=$DB"

echo "--- SOURCE BASELINE ---"
sq "$DB" "SELECT 'Members', COUNT(*) FROM Members UNION ALL SELECT 'Users', COUNT(*) FROM Users UNION ALL SELECT 'Collections', COUNT(*) FROM Collections UNION ALL SELECT 'CollAmt', ISNULL(SUM(Amount),0) FROM Collections UNION ALL SELECT 'Expenses', COUNT(*) FROM Expenses WHERE IsDeleted=0 UNION ALL SELECT 'ExpAmt', ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0 UNION ALL SELECT 'Liabs', COUNT(*) FROM SocietyLiabilities WHERE IsDeleted=0;"
sq "$DB" "SELECT TOP 1 'Migration', MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;"

# The engine runs as mssql, so the backup directory must be writable by it.
docker exec -u root sqlserver mkdir -p /var/opt/mssql/backup
docker exec -u root sqlserver chown mssql:root /var/opt/mssql/backup
docker exec -u root sqlserver chmod 755 /var/opt/mssql/backup

out=$(sq master "BACKUP DATABASE [$DB] TO DISK='/var/opt/mssql/backup/$DB.bak' WITH INIT, COMPRESSION, FORMAT;" 2>&1)
if ! echo "$out" | grep -qi 'BACKUP DATABASE successfully'; then
  echo "ABORT: backup of $DB failed"; echo "$out" | tail -n 5; exit 1
fi
echo "backed up $DB"

rm -rf /tmp/refresh && mkdir -p /tmp/refresh/uploads
docker cp "sqlserver:/var/opt/mssql/backup/$DB.bak" "/tmp/refresh/$DB.bak"

# Attachment rows are worthless without the files they point at.
docker cp smms-api:/app/uploads/. /tmp/refresh/uploads/ 2>/dev/null || echo "no uploads to copy"
echo "uploads staged: $(find /tmp/refresh/uploads -type f | wc -l) file(s)"

tar -czf /tmp/refresh.tar.gz -C /tmp/refresh .
ls -lh /tmp/refresh.tar.gz

curl -sS -X PUT -H 'x-ms-blob-type: BlockBlob' -H 'Content-Type: application/gzip' \
  --data-binary @/tmp/refresh.tar.gz "__BLOB_URL__" -w 'upload HTTP %{http_code}\n'

rm -rf /tmp/refresh /tmp/refresh.tar.gz
docker exec -u root sqlserver rm -rf /var/opt/mssql/backup
df -h / | tail -n 1
echo SOURCE_DONE

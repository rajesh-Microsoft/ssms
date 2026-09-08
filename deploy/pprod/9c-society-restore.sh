#!/bin/bash
# Runs on the PRE-PROD VM. Downloads the tarball produced by 9a and replaces ONE society's
# database with the source copy. Other societies and the control plane are never touched.
# Staging files are kept unless the restore is verified.
set -uo pipefail
cd /opt/smms/app
SOC='__SOCIETY__'

PW=$(grep '^PPROD_SA_PASSWORD=' .env | cut -d= -f2-)
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
docker exec smms-pprod-sql ls $SQLCMD >/dev/null 2>&1 || SQLCMD=/opt/mssql-tools/bin/sqlcmd
sq() { docker exec smms-pprod-sql $SQLCMD -S localhost -U sa -P "$PW" -C -b -h -1 -W -s '|' -d "$1" -Q "SET NOCOUNT ON; $2"; }

DB=$(sq SmmsControlDb "SELECT DbName FROM Societies WHERE [Key]='$SOC';" | tr -d ' \r\n')
if [ -z "$DB" ]; then echo "ABORT: society '$SOC' has no row in SmmsControlDb.Societies"; exit 1; fi
echo "society=$SOC db=$DB"

curl -fsSL "__BLOB_URL__" -o /tmp/refresh.tar.gz || { echo "ABORT: download failed"; exit 1; }
rm -rf /tmp/refresh && mkdir -p /tmp/refresh
tar -xzf /tmp/refresh.tar.gz -C /tmp/refresh
if [ ! -f "/tmp/refresh/$DB.bak" ]; then
  echo "ABORT: archive does not contain $DB.bak"; ls -l /tmp/refresh; exit 1
fi

docker exec -u root smms-pprod-sql mkdir -p /var/opt/mssql/backup
docker cp "/tmp/refresh/$DB.bak" "smms-pprod-sql:/var/opt/mssql/backup/$DB.bak" >/dev/null
# docker cp lands files root:root 0640 and the engine runs as mssql. The DIRECTORY must keep
# its execute bit: a blanket `chmod -R 644` makes every restore fail with "error 2".
docker exec -u root smms-pprod-sql chown -R mssql:root /var/opt/mssql/backup
docker exec -u root smms-pprod-sql chmod 755 /var/opt/mssql/backup
docker exec -u root smms-pprod-sql find /var/opt/mssql/backup -type f -exec chmod 644 {} +

# The API holds an open pool against this database; WITH REPLACE needs exclusive access.
echo "stopping smms-pprod-api"
docker stop smms-pprod-api >/dev/null

out=$(sq master "RESTORE DATABASE [$DB] FROM DISK='/var/opt/mssql/backup/$DB.bak' WITH REPLACE, RECOVERY;" 2>&1)
if ! echo "$out" | grep -qi 'RESTORE DATABASE successfully'; then
  echo "first attempt failed, forcing exclusive access"; echo "$out" | tail -n 3
  sq master "ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" >/dev/null 2>&1
  out=$(sq master "RESTORE DATABASE [$DB] FROM DISK='/var/opt/mssql/backup/$DB.bak' WITH REPLACE, RECOVERY;" 2>&1)
fi
sq master "ALTER DATABASE [$DB] SET MULTI_USER;" >/dev/null 2>&1

if ! echo "$out" | grep -qi 'RESTORE DATABASE successfully'; then
  echo "RESTORE FAILED"; echo "$out" | tail -n 5
  docker start smms-pprod-api >/dev/null
  echo "staging left at /tmp/refresh for retry"
  exit 1
fi
echo "restored $DB"

# Merge uploads in. Never delete the directory: it is shared by every tenant.
if [ -d /tmp/refresh/uploads ]; then
  docker cp /tmp/refresh/uploads/. smms-pprod-api:/app/uploads/ 2>/dev/null \
    && echo "uploads merged" || echo "WARNING: uploads could not be copied"
fi

docker start smms-pprod-api >/dev/null
echo "waiting for api"
for i in $(seq 1 30); do
  code=$(curl -s -o /dev/null -w '%{http_code}' --resolve "pprod-$SOC.ssms.yuvaansoft.shop:443:127.0.0.1" "https://pprod-$SOC.ssms.yuvaansoft.shop/api/health" 2>/dev/null)
  [ "$code" = "200" ] && break
  sleep 5
done
echo "health: $code"

echo "--- RESTORED STATE ---"
sq "$DB" "SELECT 'Members', COUNT(*) FROM Members UNION ALL SELECT 'Users', COUNT(*) FROM Users UNION ALL SELECT 'Collections', COUNT(*) FROM Collections UNION ALL SELECT 'CollAmt', ISNULL(SUM(Amount),0) FROM Collections UNION ALL SELECT 'Expenses', COUNT(*) FROM Expenses WHERE IsDeleted=0 UNION ALL SELECT 'ExpAmt', ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0 UNION ALL SELECT 'Liabs', COUNT(*) FROM SocietyLiabilities WHERE IsDeleted=0;"
sq "$DB" "SELECT TOP 1 'Migration', MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;"
echo "--- societies (must be unchanged) ---"
sq SmmsControlDb "SELECT [Key], DbName, Status FROM Societies ORDER BY [Key];"
echo "--- uploads ---"
docker exec smms-pprod-api sh -c 'find /app/uploads -type f | wc -l'

rm -rf /tmp/refresh /tmp/refresh.tar.gz
docker exec -u root smms-pprod-sql rm -rf /var/opt/mssql/backup
echo RESTORE_DONE

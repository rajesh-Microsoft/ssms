#!/bin/bash
# Retry of the restore using the .bak files already staged in /tmp/bak.
# Directory needs 755 (traversable); only the files get 644.
set -uo pipefail
cd /opt/smms/app
PW=$(grep '^PPROD_SA_PASSWORD=' .env | cut -d= -f2-)

sq() { docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -b -h -1 -W -s '|' -Q "SET NOCOUNT ON; $1"; }

docker exec -u root smms-pprod-sql mkdir -p /var/opt/mssql/backup
docker cp /tmp/bak/. smms-pprod-sql:/var/opt/mssql/backup/ >/dev/null
docker exec -u root smms-pprod-sql chown -R mssql:root /var/opt/mssql/backup
docker exec -u root smms-pprod-sql chmod 755 /var/opt/mssql/backup
docker exec -u root smms-pprod-sql find /var/opt/mssql/backup -type f -exec chmod 644 {} +
echo "--- perms in container ---"
docker exec -u root smms-pprod-sql ls -ld /var/opt/mssql/backup
docker exec -u root smms-pprod-sql ls -l /var/opt/mssql/backup | head -n 3

fail=0
for f in /tmp/bak/*.bak; do
  db=$(basename "$f" .bak)
  out=$(sq "RESTORE DATABASE [$db] FROM DISK='/var/opt/mssql/backup/$db.bak' WITH REPLACE, RECOVERY;" 2>&1)
  if echo "$out" | grep -qi 'RESTORE DATABASE successfully'; then
    echo "OK   $db"
  else
    echo "FAIL $db"; echo "$out" | tail -n 3; fail=1
  fi
done

if [ -d /tmp/bak/uploads ]; then
  docker cp /tmp/bak/uploads/. smms-pprod-api:/app/uploads/ 2>/dev/null \
    && echo "uploads restored: $(docker exec smms-pprod-api sh -c 'find /app/uploads -type f | wc -l') file(s)" \
    || echo "WARNING: uploads present in the archive but could not be copied into smms-pprod-api"
fi

echo "--- databases ---"
sq "SELECT name, state_desc FROM sys.databases WHERE name LIKE 'Smms%' ORDER BY name;"echo "--- societies ---"
docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W -s '|' -d SmmsControlDb \
  -Q "SET NOCOUNT ON; SELECT [Key], DbName, Status FROM Societies ORDER BY [Key];"
echo "--- aadya sanity (should match UAT: 25 members, 304 collections, 11 liabilities) ---"
docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W -s '|' -d SmmsDb_Aadya \
  -Q "SET NOCOUNT ON; SELECT 'Members', COUNT(*) FROM Members UNION ALL SELECT 'Collections', COUNT(*) FROM Collections UNION ALL SELECT 'Liabilities', COUNT(*) FROM SocietyLiabilities;"

if [ "$fail" -eq 0 ]; then
  rm -rf /tmp/bak /tmp/smms-dbs.tar.gz
  docker exec -u root smms-pprod-sql rm -rf /var/opt/mssql/backup
  echo RESTORE_DONE_CLEANED
else
  echo "RESTORE_HAD_FAILURES - staging files left in /tmp/bak"
fi

#!/bin/bash
# Runs on the PRE-PROD VM: downloads the tarball and restores every database.
# Cleanup happens only after the restore is verified.
set -uo pipefail
cd /opt/smms/app
PW=$(grep '^PPROD_SA_PASSWORD=' .env | cut -d= -f2-)

sq() { docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -b -h -1 -W -s '|' -Q "SET NOCOUNT ON; $1"; }

curl -fsSL "__BLOB_URL__" -o /tmp/smms-dbs.tar.gz
rm -rf /tmp/bak && mkdir -p /tmp/bak
tar -xzf /tmp/smms-dbs.tar.gz -C /tmp/bak

docker exec -u root smms-pprod-sql mkdir -p /var/opt/mssql/backup
docker cp /tmp/bak/. smms-pprod-sql:/var/opt/mssql/backup/ >/dev/null
# docker cp lands files as root:root 0640; the engine runs as mssql and cannot read them.
# The DIRECTORY must stay traversable — a blanket `chmod -R 644` removes its execute bit and
# every restore then fails with "Cannot open backup device ... error 2".
docker exec -u root smms-pprod-sql chown -R mssql:root /var/opt/mssql/backup
docker exec -u root smms-pprod-sql chmod 755 /var/opt/mssql/backup
docker exec -u root smms-pprod-sql find /var/opt/mssql/backup -type f -exec chmod 644 {} +

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

# Uploaded evidence rides along with the databases; restored attachment rows would otherwise
# point at files that do not exist here.
if [ -d /tmp/bak/uploads ]; then
  docker cp /tmp/bak/uploads/. smms-pprod-api:/app/uploads/ 2>/dev/null \
    && echo "uploads restored: $(docker exec smms-pprod-api sh -c 'find /app/uploads -type f | wc -l') file(s)" \
    || echo "WARNING: uploads present in the archive but could not be copied into smms-pprod-api"
else
  echo "no uploads in the archive"
fi

echo "--- databases ---"
sq "SELECT name, state_desc FROM sys.databases WHERE name LIKE 'Smms%' ORDER BY name;"
echo "--- societies ---"
docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W -s '|' -d SmmsControlDb \
  -Q "SET NOCOUNT ON; SELECT [Key], DbName, Status FROM Societies ORDER BY [Key];"
echo "--- aadya row counts ---"
docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W -s '|' -d SmmsDb_Aadya \
  -Q "SET NOCOUNT ON; SELECT 'Members', COUNT(*) FROM Members UNION ALL SELECT 'Collections', COUNT(*) FROM Collections UNION ALL SELECT 'Liabilities', COUNT(*) FROM SocietyLiabilities;"

if [ "$fail" -eq 0 ]; then
  rm -rf /tmp/bak /tmp/smms-dbs.tar.gz
  docker exec -u root smms-pprod-sql rm -rf /var/opt/mssql/backup
  echo RESTORE_DONE_CLEANED
else
  echo "RESTORE_HAD_FAILURES - staging files left in /tmp/bak for retry"
fi

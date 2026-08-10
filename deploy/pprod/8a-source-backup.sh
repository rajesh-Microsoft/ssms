#!/bin/bash
# Runs on the SOURCE VM: backs up every SMMS database and uploads one tarball.
set -euo pipefail
PW=$(docker exec smms-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p')
DBS="SmmsControlDb SmmsDb SmmsDb_Aadya SmmsDb_Aaradya SmmsDb_Brij-a SmmsDb_Demo SmmsDb_Ireddy SmmsDb_Lake-view SmmsDb_Sunrise SmmsDb_Vr"

docker exec sqlserver mkdir -p /var/opt/mssql/backup
for db in $DBS; do
  docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -b \
    -Q "BACKUP DATABASE [$db] TO DISK='/var/opt/mssql/backup/$db.bak' WITH INIT, COMPRESSION, FORMAT;" >/dev/null
  echo "backed up $db"
done

rm -rf /tmp/bak && mkdir -p /tmp/bak/uploads
docker cp sqlserver:/var/opt/mssql/backup/. /tmp/bak/

# Uploaded evidence travels with the databases. Without it, restored attachment rows point at
# files that do not exist on the target.
docker cp smms-api:/app/uploads/. /tmp/bak/uploads/ 2>/dev/null || echo "no uploads to copy"
echo "uploads: $(find /tmp/bak/uploads -type f | wc -l) file(s)"

tar -czf /tmp/smms-dbs.tar.gz -C /tmp/bak .
ls -lh /tmp/smms-dbs.tar.gz

curl -sS -X PUT -H 'x-ms-blob-type: BlockBlob' -H 'Content-Type: application/gzip' \
  --data-binary @/tmp/smms-dbs.tar.gz "__BLOB_URL__" -w 'upload HTTP %{http_code}\n'

rm -rf /tmp/bak /tmp/smms-dbs.tar.gz
docker exec sqlserver rm -rf /var/opt/mssql/backup
echo SOURCE_DONE

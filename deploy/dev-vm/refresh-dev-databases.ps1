# Starts the dev SQL container, then clones the UAT databases into it, so dev tests run
# against realistic data. Reads on UAT are BACKUP DATABASE only - online and non-blocking.
# Nothing in ~/smms is written to.
$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
set -e
cd ~/smms-dev/SMMS

echo "===== START DEV SQL ONLY ====="
docker compose up -d sqlserver
echo "waiting for dev SQL to accept connections..."
DEV_SA=$(grep '^DEV_SA_PASSWORD=' .env | cut -d= -f2-)
for i in $(seq 1 60); do
  if docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd \
       -S localhost -U sa -P "$DEV_SA" -C -Q "SELECT 1" >/dev/null 2>&1; then
    echo "dev SQL ready after ${i}s"; break
  fi
  sleep 1
done

PROD_SA=$(docker exec sqlserver printenv SA_PASSWORD)
DBS="SmmsControlDb SmmsDb SmmsDb_Aadya SmmsDb_Aaradya SmmsDb_Brij-a SmmsDb_Sunrise SmmsDb_Lake-view SmmsDb_Ireddy SmmsDb_Vr SmmsDb_Demo"

echo "===== BACKUP PRODUCTION ====="
docker exec -u root sqlserver mkdir -p /var/opt/mssql/backup
docker exec -u root sqlserver chown mssql /var/opt/mssql/backup
for db in $DBS; do
  docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PROD_SA" -C -b \
    -Q "BACKUP DATABASE [$db] TO DISK='/var/opt/mssql/backup/$db.bak' WITH INIT, FORMAT, COMPRESSION;" >/dev/null
  echo "  backed up $db"
done

echo "===== TRANSFER ====="
rm -rf /tmp/smmsbak && mkdir -p /tmp/smmsbak
docker exec -u root smms-dev-sql mkdir -p /var/opt/mssql/backup
for db in $DBS; do
  docker cp "sqlserver:/var/opt/mssql/backup/$db.bak" /tmp/smmsbak/ >/dev/null
  docker cp "/tmp/smmsbak/$db.bak" "smms-dev-sql:/var/opt/mssql/backup/" >/dev/null
done
docker exec -u root smms-dev-sql chown -R mssql /var/opt/mssql/backup
du -sh /tmp/smmsbak

echo "===== RESTORE INTO DEV ====="
for db in $DBS; do
  docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -b \
    -Q "RESTORE DATABASE [$db] FROM DISK='/var/opt/mssql/backup/$db.bak' WITH REPLACE, RECOVERY;" >/dev/null
  echo "  restored $db"
done

echo "===== CLEAN UP BACKUP FILES ====="
docker exec -u root sqlserver rm -rf /var/opt/mssql/backup
docker exec -u root smms-dev-sql rm -rf /var/opt/mssql/backup
rm -rf /tmp/smmsbak

echo "===== DEV DATABASES ====="
docker exec smms-dev-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DEV_SA" -C -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE database_id > 4;"

echo "===== PRODUCTION UNTOUCHED CHECK ====="
docker ps --format '{{.Names}} {{.Status}}' | grep -E '^(sqlserver|smms-api|smms-ui) '
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

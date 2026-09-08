#!/bin/bash
# Runs on the PRE-PROD VM BEFORE the refresh. Takes a restorable copy of the society
# database that is about to be overwritten, and proves it with RESTORE VERIFYONLY.
# The refresh must not proceed if this fails.
set -uo pipefail
cd /opt/smms/app
SOC='__SOCIETY__'
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
DEST=/opt/smms/backups

PW=$(grep '^PPROD_SA_PASSWORD=' .env | cut -d= -f2-)
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
docker exec smms-pprod-sql ls $SQLCMD >/dev/null 2>&1 || SQLCMD=/opt/mssql-tools/bin/sqlcmd
sq() { docker exec smms-pprod-sql $SQLCMD -S localhost -U sa -P "$PW" -C -b -h -1 -W -s '|' -d "$1" -Q "SET NOCOUNT ON; $2"; }

DB=$(sq SmmsControlDb "SELECT DbName FROM Societies WHERE [Key]='$SOC';" | tr -d ' \r\n')
if [ -z "$DB" ]; then echo "ABORT: society '$SOC' has no row in SmmsControlDb.Societies"; exit 1; fi
echo "society=$SOC db=$DB"

echo "--- PRE-PROD STATE ABOUT TO BE REPLACED ---"
sq "$DB" "SELECT 'Members', COUNT(*) FROM Members UNION ALL SELECT 'Users', COUNT(*) FROM Users UNION ALL SELECT 'Collections', COUNT(*) FROM Collections UNION ALL SELECT 'CollAmt', ISNULL(SUM(Amount),0) FROM Collections UNION ALL SELECT 'Expenses', COUNT(*) FROM Expenses WHERE IsDeleted=0 UNION ALL SELECT 'Liabs', COUNT(*) FROM SocietyLiabilities WHERE IsDeleted=0 UNION ALL SELECT 'Reimbursements', COUNT(*) FROM ReimbursementRequests;"

mkdir -p "$DEST"
docker exec -u root smms-pprod-sql mkdir -p /var/opt/mssql/backup
docker exec -u root smms-pprod-sql chown mssql:root /var/opt/mssql/backup
docker exec -u root smms-pprod-sql chmod 755 /var/opt/mssql/backup

out=$(sq master "BACKUP DATABASE [$DB] TO DISK='/var/opt/mssql/backup/safety.bak' WITH INIT, COMPRESSION, FORMAT;" 2>&1)
if ! echo "$out" | grep -qi 'BACKUP DATABASE successfully'; then
  echo "ABORT: safety backup of $DB failed"; echo "$out" | tail -n 5; exit 1
fi

ver=$(sq master "RESTORE VERIFYONLY FROM DISK='/var/opt/mssql/backup/safety.bak';" 2>&1)
if ! echo "$ver" | grep -qi 'backup set on file 1 is valid'; then
  echo "ABORT: safety backup did not verify"; echo "$ver" | tail -n 5; exit 1
fi

FILE="$DEST/pprod-$SOC-before-refresh-$STAMP.bak"
docker cp smms-pprod-sql:/var/opt/mssql/backup/safety.bak "$FILE"
docker exec -u root smms-pprod-sql rm -f /var/opt/mssql/backup/safety.bak
ls -lh "$FILE"
echo "SAFETY_BACKUP_OK $FILE"

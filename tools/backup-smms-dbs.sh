#!/usr/bin/env bash
# Full backup of every SMMS database out of the SQL Server container to a
# timestamped tarball on the host. Requires SA_PASSWORD in the environment.
set -euo pipefail

CONTAINER=sqlserver
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
CONTAINER_BAK_DIR=/var/opt/mssql/backup
: "${SA_PASSWORD:?SA_PASSWORD must be set}"

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
ROOT="${HOME}/smms-backups"
OUTDIR="${ROOT}/${STAMP}"
mkdir -p "$OUTDIR"

sql() {
  sudo docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C "$@"
}

sudo docker exec "$CONTAINER" mkdir -p "$CONTAINER_BAK_DIR"

DBS=$(sql -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE database_id > 4" \
  | sed '/^$/d;/rows affected/d')

echo "Databases to back up:"
echo "$DBS"
echo

for DB in $DBS; do
  echo ">>> $DB"
  sql -Q "BACKUP DATABASE [$DB] TO DISK = N'${CONTAINER_BAK_DIR}/${DB}.bak' WITH FORMAT, INIT, COMPRESSION, CHECKSUM, STATS = 50"
  # Fail loudly if the backup file is not restorable.
  sql -Q "RESTORE VERIFYONLY FROM DISK = N'${CONTAINER_BAK_DIR}/${DB}.bak' WITH CHECKSUM"
  sudo docker cp "${CONTAINER}:${CONTAINER_BAK_DIR}/${DB}.bak" "${OUTDIR}/${DB}.bak"
done

sudo chown -R "$(id -u):$(id -g)" "$OUTDIR"

cd "$ROOT"
tar -czf "smms-backup-${STAMP}.tar.gz" "$STAMP"
sha256sum "smms-backup-${STAMP}.tar.gz" > "smms-backup-${STAMP}.tar.gz.sha256"
rm -rf "$OUTDIR"

echo
echo "DONE: ${ROOT}/smms-backup-${STAMP}.tar.gz"
ls -lh "${ROOT}/smms-backup-${STAMP}.tar.gz"

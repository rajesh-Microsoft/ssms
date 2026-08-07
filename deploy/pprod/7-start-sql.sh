#!/bin/bash
set -euo pipefail
cd /opt/smms/app
docker compose up -d sqlserver
echo "--- waiting for SQL Server ---"
PW=$(grep '^PPROD_SA_PASSWORD=' .env | cut -d= -f2-)
for i in $(seq 1 40); do
  if docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -Q "SELECT 1" >/dev/null 2>&1; then
    echo "SQL ready after ${i}0s"; break
  fi
  sleep 10
done
docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W -Q "SET NOCOUNT ON; SELECT @@VERSION;" | head -n 2
docker ps --format '{{.Names}} {{.Status}}'

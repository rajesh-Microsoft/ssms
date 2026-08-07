#!/bin/bash
set -euo pipefail
PW=$(docker exec smms-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p')
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d SmmsControlDb -h -1 -W -s "|" \
  -Q "SET NOCOUNT ON; SELECT [Key], DbName, Status FROM Societies ORDER BY [Key];"
echo "--- databases on server ---"
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'Smms%' ORDER BY name;"

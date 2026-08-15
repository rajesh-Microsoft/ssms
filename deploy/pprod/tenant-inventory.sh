#!/bin/bash
# Which societies the pre-prod control plane actually serves, and which databases exist.
PW=$(docker exec smms-pprod-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p' | tr -d '\r\n')
echo "===== control plane societies ====="
docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d SmmsControlDb -h -1 -W -s '|' \
  -Q "SET NOCOUNT ON; SELECT [Key], DbName, Status FROM Societies ORDER BY [Key];" 2>/dev/null | tr -d '\r'
echo "===== databases on the server ====="
docker exec smms-pprod-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'SmmsDb[_]%' ORDER BY name;" 2>/dev/null | tr -d '\r'
echo "===== api startup: which tenants were readied ====="
docker logs smms-pprod-api 2>&1 | grep -E 'Database is ready|skipping|not active' | tail -n 20
echo "__PROMOTE_OK__"

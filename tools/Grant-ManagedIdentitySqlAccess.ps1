<#
.SYNOPSIS
    Grants the SMMS managed identity access to the Azure SQL server and its databases.

.DESCRIPTION
    Bicep can create the SQL server, set its Entra admin, and create databases, but it
    cannot create database users - those live inside each database, not in ARM. This
    script closes that gap and must be run once after the infrastructure deployment,
    and again after each new society is onboarded.

    Two distinct grants are applied:

      master        CREATE USER + dbmanager role. Without dbmanager the tenant
                    provisioner cannot issue CREATE DATABASE, so onboarding a new
                    society fails outright.

      each database CREATE USER + db_owner. EF Core migrations create and alter
                    tables on startup, which needs DDL rights.

    Everything is idempotent, so re-running after adding a society is safe and only
    touches the databases that are missing the user.

    You must be signed in with the Azure CLI as a member of the server's Entra admin
    group (the group passed to Bicep as sqlAdminObjectId). Authentication uses an access
    token taken from that CLI session, so there is no password and no interactive prompt
    - which also means it works for guest accounts subject to MFA, where sqlcmd's
    integrated Entra auth fails.

.PARAMETER SqlServerFqdn
    Logical server, e.g. "smms-prod-sql-abc123.database.windows.net".
    This is the sqlServerFqdn output of the Bicep deployment.

.PARAMETER IdentityName
    Name of the user-assigned managed identity, e.g. "smms-prod-id".
    This is the identityName output of the Bicep deployment.

.PARAMETER Database
    Specific databases to grant. Omit to discover every database on the server.

.PARAMETER SkipMaster
    Skip the master-level dbmanager grant. Only useful when re-running purely to pick
    up newly created tenant databases.

.EXAMPLE
    # First run after deploying the infrastructure
    .\Grant-ManagedIdentitySqlAccess.ps1 `
        -SqlServerFqdn "smms-prod-sql-abc123.database.windows.net" `
        -IdentityName "smms-prod-id"

.EXAMPLE
    # After onboarding a new society
    .\Grant-ManagedIdentitySqlAccess.ps1 `
        -SqlServerFqdn "smms-prod-sql-abc123.database.windows.net" `
        -IdentityName "smms-prod-id" -Database "SmmsDb_Newsociety" -SkipMaster

.EXAMPLE
    # Drive it straight off the deployment outputs
    $o = az deployment group show -g smms-prod-rg -n main --query properties.outputs | ConvertFrom-Json
    .\Grant-ManagedIdentitySqlAccess.ps1 -SqlServerFqdn $o.sqlServerFqdn.value -IdentityName $o.identityName.value
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SqlServerFqdn,
    [Parameter(Mandatory)][string]$IdentityName,
    [string[]]$Database,
    [switch]$SkipMaster
)

$ErrorActionPreference = 'Stop'

# The name is concatenated into DDL, where it cannot be a bind parameter.
if ($IdentityName -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*$') {
    throw "IdentityName '$IdentityName' is not a valid identity name."
}

function Initialize-SqlClient {
    if ('Microsoft.Data.SqlClient.SqlConnection' -as [type]) { return }

    # The assembly in the build output root is a facade that throws on Windows; the real
    # implementation lives under runtimes\win and needs its native SNI library beside it.
    $binRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'api\SMMS.Api\bin'
    if (-not (Test-Path $binRoot)) {
        throw "Build output not found at '$binRoot'. Run 'dotnet build api/SMMS.Api' first."
    }

    $lib = Get-ChildItem $binRoot -Recurse -Filter 'Microsoft.Data.SqlClient.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\runtimes\\win\\lib\\' } | Select-Object -First 1
    $sni = Get-ChildItem $binRoot -Recurse -Filter 'Microsoft.Data.SqlClient.SNI.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\win-x64\\' } | Select-Object -First 1
    if (-not $lib -or -not $sni) {
        throw "Could not locate the Windows build of Microsoft.Data.SqlClient under '$binRoot'."
    }

    $stage = Join-Path $env:TEMP 'smms-sqlclient'
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item $lib.FullName $stage -Force
    Copy-Item $sni.FullName $stage -Force
    Add-Type -Path (Join-Path $stage 'Microsoft.Data.SqlClient.dll')
}

function Get-SqlAccessToken {
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw "Could not obtain a SQL access token. Run 'az login' and retry."
    }
    return $token.Trim()
}

function Open-SqlConnection {
    param([string]$DatabaseName)

    $conn = [Microsoft.Data.SqlClient.SqlConnection]::new(
        "Server=tcp:$SqlServerFqdn,1433;Database=$DatabaseName;Encrypt=True;Connect Timeout=30")
    $conn.AccessToken = $script:SqlToken
    $conn.Open()
    return $conn
}

function Invoke-Sql {
    param([string]$DatabaseName, [string]$Query)

    $conn = Open-SqlConnection -DatabaseName $DatabaseName
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Query
        $cmd.CommandTimeout = 120
        $cmd.ExecuteNonQuery() | Out-Null
    }
    finally { $conn.Dispose() }
}

function Get-SqlStrings {
    param([string]$DatabaseName, [string]$Query)

    $conn = Open-SqlConnection -DatabaseName $DatabaseName
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Query
        $cmd.CommandTimeout = 120
        $reader = $cmd.ExecuteReader()
        $values = while ($reader.Read()) { $reader.GetString(0) }
        $reader.Close()
        return $values
    }
    finally { $conn.Dispose() }
}

Initialize-SqlClient
$script:SqlToken = Get-SqlAccessToken

function New-GrantScript {
    param([string]$RoleName)

    # CREATE USER FROM EXTERNAL PROVIDER resolves the name against Entra, so the identity
    # must already exist. Both steps are guarded to keep the script re-runnable.
    @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$IdentityName')
    CREATE USER [$IdentityName] FROM EXTERNAL PROVIDER;

IF NOT EXISTS (
    SELECT 1
    FROM sys.database_role_members rm
    JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
    JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
    WHERE r.name = N'$RoleName' AND m.name = N'$IdentityName')
    ALTER ROLE [$RoleName] ADD MEMBER [$IdentityName];
"@
}

Write-Host "Server   : $SqlServerFqdn"
Write-Host "Identity : $IdentityName"
Write-Host ""

if (-not $SkipMaster) {
    Write-Host "master ... " -NoNewline
    Invoke-Sql -DatabaseName 'master' -Query (New-GrantScript -RoleName 'dbmanager')
    Write-Host "dbmanager granted" -ForegroundColor Green
}

if (-not $Database) {
    $Database = Get-SqlStrings -DatabaseName 'master' -Query `
        "SELECT name FROM sys.databases WHERE name <> 'master' ORDER BY name;"
}

if (-not $Database) {
    Write-Warning "No databases found on $SqlServerFqdn. Deploy the infrastructure first."
    return
}

$failed = @()
foreach ($db in $Database) {
    Write-Host "$db ... " -NoNewline
    try {
        Invoke-Sql -DatabaseName $db -Query (New-GrantScript -RoleName 'db_owner')
        Write-Host "db_owner granted" -ForegroundColor Green
    }
    catch {
        Write-Host "FAILED" -ForegroundColor Red
        Write-Host "  $($_.Exception.Message)" -ForegroundColor DarkGray
        $failed += $db
    }
}

Write-Host ""
if ($failed) {
    throw "Failed on: $($failed -join ', ')"
}
Write-Host "All grants applied." -ForegroundColor Green

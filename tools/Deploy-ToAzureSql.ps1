<#
.SYNOPSIS
    Creates the SMMS database schema on Azure SQL and copies data from the local
    LocalDB database (SmmsDb) into it.

.DESCRIPTION
    Two independent phases:
      1. Schema  - generates an idempotent SQL script from the EF Core migrations
                   (api/SMMS.Api/Migrations) via `dotnet ef migrations script`, then
                   runs it against the target Azure SQL database with sqlcmd.
      2. Data    - copies every table's rows from the local database to Azure SQL,
                   table by table, in FK-safe order, using bcp (preserving identity
                   values with -E).

    Requires on PATH: dotnet-ef (dotnet tool), sqlcmd, bcp.
    (All three were verified present on this machine: sqlcmd/bcp 15.0, dotnet-ef 10.0.0.)

    Make sure the Azure SQL server's firewall allows your client IP
    (Azure Portal -> SQL server -> Networking -> Add client IP), before running this.

.PARAMETER AzureSqlServer
    Azure SQL logical server, e.g. "myserver.database.windows.net".

.PARAMETER AzureSqlDatabase
    Target database name on the Azure SQL server. Created automatically by the
    Azure portal/az cli beforehand — this script does not create the database itself.

.PARAMETER AzureSqlUser
    SQL authentication login (server admin or a user with db_owner on the target DB).

.PARAMETER AzureSqlPassword
    SQL authentication password. Pass as SecureString for safety; a plain string is
    also accepted for convenience but avoid typing it directly in shared terminals/history.

.PARAMETER LocalSqlServer
    Local SQL Server/LocalDB instance. Defaults to "(localdb)\MSSQLLocalDB".

.PARAMETER LocalDatabase
    Local database name. Defaults to "SmmsDb".

.PARAMETER SchemaOnly
    Only run phase 1 (create schema on Azure SQL). Skips data copy.

.PARAMETER DataOnly
    Only run phase 2 (copy data). Skips schema creation — use when the schema
    already exists on the target.

.PARAMETER Force
    Skip the confirmation prompt and the "target tables must be empty" safety check
    before copying data. Without -Force, the script aborts the data-copy phase if any
    target table already has rows, to avoid duplicate/conflicting primary keys.

.EXAMPLE
    # Full deploy: schema + data
    .\Deploy-ToAzureSql.ps1 -AzureSqlServer "smms-sql.database.windows.net" `
        -AzureSqlDatabase "SmmsDb" -AzureSqlUser "smmsadmin" -AzureSqlPassword "P@ssword123"

.EXAMPLE
    # Only push schema changes after a new EF Core migration
    .\Deploy-ToAzureSql.ps1 -AzureSqlServer "smms-sql.database.windows.net" `
        -AzureSqlDatabase "SmmsDb" -AzureSqlUser "smmsadmin" -AzureSqlPassword "P@ssword123" -SchemaOnly
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$AzureSqlServer,

    [string]$AzureSqlDatabase,

    [string]$AzureSqlUser,

    [object]$AzureSqlPassword,

    [string]$LocalSqlServer = "(localdb)\MSSQLLocalDB",

    [string]$LocalDatabase = "SmmsDb",

    [switch]$SchemaOnly,

    [switch]$DataOnly,

    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Fail fast with a clear message instead of falling back to PowerShell's interactive
# "Supply values for the following parameters" prompt (which hangs/misbehaves when
# this script is run non-interactively, e.g. from a task or CI).
$missing = @()
if ([string]::IsNullOrWhiteSpace($AzureSqlServer)) { $missing += "AzureSqlServer" }
if ([string]::IsNullOrWhiteSpace($AzureSqlDatabase)) { $missing += "AzureSqlDatabase" }
if ([string]::IsNullOrWhiteSpace($AzureSqlUser)) { $missing += "AzureSqlUser" }
if (-not $AzureSqlPassword) { $missing += "AzureSqlPassword" }
if ($missing.Count -gt 0) {
    Write-Host @"
Missing required parameter(s): $($missing -join ', ')

Usage:
  .\Deploy-ToAzureSql.ps1 -AzureSqlServer "yourserver.database.windows.net" ``
      -AzureSqlDatabase "SmmsDb" -AzureSqlUser "smmsadmin" -AzureSqlPassword "YourPassword"

Add -SchemaOnly or -DataOnly to run just one phase. See Get-Help .\Deploy-ToAzureSql.ps1 -Full for details.
"@ -ForegroundColor Red
    exit 1
}

# Resolve a SecureString password down to plain text for tool invocation (sqlcmd/bcp need plain args).
if ($AzureSqlPassword -is [System.Security.SecureString]) {
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($AzureSqlPassword)
    try {
        $AzureSqlPasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}
else {
    $AzureSqlPasswordPlain = [string]$AzureSqlPassword
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "api\SMMS.Api"

if (-not (Test-Path $ProjectPath)) {
    throw "Could not find project at '$ProjectPath'. Run this script from its default location under tools\."
}

# Tables in FK-safe order: parents before children.
# Collections -> Members, Complaints -> Users. Everything else is standalone.
$TableOrder = @("Users", "Members", "Settings", "Expenses", "AuditLog", "Collections", "Complaints")

function Invoke-Checked {
    param([string]$Exe, [string[]]$Args, [string]$Description)
    Write-Host "`n>> $Description" -ForegroundColor Cyan
    Write-Host "   $Exe $($Args -join ' ')" -ForegroundColor DarkGray
    & $Exe @Args
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed (exit code $LASTEXITCODE)."
    }
}

function Deploy-Schema {
    Write-Host "`n=== Phase 1: Deploy schema to Azure SQL ($AzureSqlDatabase on $AzureSqlServer) ===" -ForegroundColor Yellow

    $scriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "smms-schema-$(Get-Date -Format 'yyyyMMddHHmmss').sql"

    Push-Location $ProjectPath
    try {
        Invoke-Checked -Exe "dotnet" -Args @("ef", "migrations", "script", "--idempotent", "--output", $scriptPath) `
            -Description "Generating idempotent schema script from EF Core migrations"
    }
    finally {
        Pop-Location
    }

    if ($PSCmdlet.ShouldProcess("$AzureSqlServer/$AzureSqlDatabase", "Apply schema script")) {
        Invoke-Checked -Exe "sqlcmd" -Args @(
            "-S", $AzureSqlServer, "-d", $AzureSqlDatabase,
            "-U", $AzureSqlUser, "-P", $AzureSqlPasswordPlain,
            "-I", "-b", "-i", $scriptPath
        ) -Description "Applying schema script to Azure SQL"
    }

    Remove-Item $scriptPath -ErrorAction SilentlyContinue
    Write-Host "Schema deployment complete." -ForegroundColor Green
}

function Get-RemoteRowCount {
    param([string]$Table)
    $out = & sqlcmd -S $AzureSqlServer -d $AzureSqlDatabase -U $AzureSqlUser -P $AzureSqlPasswordPlain `
        -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.[$Table]"
    if ($LASTEXITCODE -ne 0) { throw "Could not query row count for table '$Table' on Azure SQL." }
    ($out | Select-Object -First 1) -as [int]
}

function Copy-Data {
    Write-Host "`n=== Phase 2: Copy data from local '$LocalDatabase' to Azure SQL '$AzureSqlDatabase' ===" -ForegroundColor Yellow

    if (-not $Force) {
        $nonEmpty = @()
        foreach ($table in $TableOrder) {
            $count = Get-RemoteRowCount -Table $table
            if ($count -gt 0) { $nonEmpty += "$table ($count rows)" }
        }
        if ($nonEmpty.Count -gt 0) {
            throw "Target tables already contain data: $($nonEmpty -join ', '). Aborting to avoid duplicate/conflicting keys. Re-run with -Force to proceed anyway (not recommended unless target is meant to be appended to)."
        }
    }

    if (-not $PSCmdlet.ShouldProcess("$AzureSqlServer/$AzureSqlDatabase", "Bulk-copy data for tables: $($TableOrder -join ', ')")) {
        return
    }

    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "smms-bcp-$(Get-Date -Format 'yyyyMMddHHmmss')"
    New-Item -ItemType Directory -Path $tempDir | Out-Null

    try {
        foreach ($table in $TableOrder) {
            $dataFile = Join-Path $tempDir "$table.dat"

            Invoke-Checked -Exe "bcp" -Args @(
                "dbo.$table", "out", $dataFile,
                "-S", $LocalSqlServer, "-d", $LocalDatabase,
                "-T", "-n", "-k"
            ) -Description "Exporting local table dbo.$table"

            Invoke-Checked -Exe "bcp" -Args @(
                "dbo.$table", "in", $dataFile,
                "-S", $AzureSqlServer, "-d", $AzureSqlDatabase,
                "-U", $AzureSqlUser, "-P", $AzureSqlPasswordPlain,
                "-n", "-k", "-E"
            ) -Description "Importing into Azure SQL table dbo.$table (preserving identity values)"
        }
    }
    finally {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Data copy complete." -ForegroundColor Green
}

if ($DataOnly -and $SchemaOnly) {
    throw "Specify at most one of -SchemaOnly / -DataOnly."
}

if (-not $DataOnly) { Deploy-Schema }
if (-not $SchemaOnly) { Copy-Data }

Write-Host "`nDone." -ForegroundColor Green

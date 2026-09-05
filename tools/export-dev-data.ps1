[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [string[]]$SocietyKeys = @(),

    [switch]$AcknowledgePii
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $AcknowledgePii) {
    throw 'This export contains resident PII, financial records, and password hashes. Re-run with -AcknowledgePii only when the recipient is authorized.'
}

foreach ($key in $SocietyKeys) {
    if ($key -notmatch '^[a-z0-9-]+$') {
        throw "Invalid society key '$key'."
    }
}

foreach ($command in @('docker', 'age', 'tar')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        if ($command -eq 'age') {
            throw 'age is required. Install it with: winget install --id FiloSottile.age'
        }
        throw "$command is required but was not found."
    }
}

$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

$sqlContainer = 'sqlserver'
$sqlcmd = '/opt/mssql-tools18/bin/sqlcmd'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupDir = "/var/opt/mssql/dev-export-$stamp"
$stage = Join-Path ([IO.Path]::GetTempPath()) "smms-dev-export-$stamp"
$plainArchive = Join-Path ([IO.Path]::GetTempPath()) "smms-dev-data-$stamp.tar"
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$encryptedBundle = Join-Path $outputRoot "smms-dev-data-$stamp.tar.age"
$apiWasStopped = $false

function Invoke-Sql([string]$Database, [string]$Query, [switch]$Raw) {
    $arguments = @(
        'exec', $sqlContainer, 'sh', '-c',
        'SQLCMDPASSWORD="$SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd "$@"', 'sh',
        '-S', 'localhost', '-U', 'sa',
        '-C', '-b', '-d', $Database,
        '-w', '65535'
    )
    if ($Raw) {
        $arguments += @('-y', '0', '-Q', "SET NOCOUNT ON; SELECT ($Query) AS [SMMS_JSON];")
    }
    else {
        $arguments += @('-h', '-1', '-W', '-Q', "SET NOCOUNT ON; $Query")
    }
    $result = & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "SQL command failed against $Database."
    }
    if ($Raw) {
        $trimmedLines = @($result | ForEach-Object { $_.Trim() })
        $headerIndex = [Array]::IndexOf([string[]]$trimmedLines, 'SMMS_JSON')
        $payload = if ($headerIndex -ge 0) { $trimmedLines | Select-Object -Skip ($headerIndex + 2) } else { $trimmedLines }
        $json = ($payload -join "`n").Trim()
        if (-not $json) { throw "SQL JSON output was empty against $Database." }
        return $json
    }
    return @($result | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Get-TableCounts([string]$Database) {
    $query = @"
SELECT [TableName], [RowCount]
FROM (
    SELECT 'Users' AS [TableName], COUNT_BIG(*) AS [RowCount] FROM dbo.Users
    UNION ALL SELECT 'Members', COUNT_BIG(*) FROM dbo.Members
    UNION ALL SELECT 'Collections', COUNT_BIG(*) FROM dbo.Collections
    UNION ALL SELECT 'Expenses', COUNT_BIG(*) FROM dbo.Expenses
) counts
ORDER BY [TableName]
FOR JSON PATH
"@
    $json = Invoke-Sql -Database $Database -Query $query -Raw
    return @($json | ConvertFrom-Json)
}

New-Item -ItemType Directory -Force -Path $outputRoot, $stage | Out-Null

try {
    & docker info *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop is not ready.' }

    & docker compose stop smms-api
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop smms-api; export aborted before reading metadata or taking backups.' }
    $apiWasStopped = $true

    $where = if ($SocietyKeys.Count -gt 0) {
        $quoted = $SocietyKeys | ForEach-Object { "N'$($_.Replace("'", "''"))'" }
        "WHERE [Status] <> N'Pending' AND [Key] IN ($($quoted -join ','))"
    }
    else { "WHERE [Status] <> N'Pending'" }

    $societyQuery = @"
SELECT [Key], DisplayName, DbName, [Status], [Plan], ExpiryDate, FlatCount, IsDemo, CreatedAt
FROM dbo.Societies
$where
ORDER BY [Key]
FOR JSON PATH, INCLUDE_NULL_VALUES
"@
    $societyJson = Invoke-Sql -Database 'SmmsControlDb' -Query $societyQuery -Raw
    $societies = @($societyJson | ConvertFrom-Json)
    if ($societies.Count -eq 0) { throw 'No matching societies were found in SmmsControlDb.' }

    foreach ($society in $societies) {
        if ([string]$society.DbName -notmatch '^SmmsDb_[A-Za-z0-9-]+$') {
            throw "Unsafe database name registered for society '$($society.Key)': $($society.DbName)"
        }
    }

    $availableDatabases = @(Invoke-Sql -Database 'master' -Query "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE';")
    $missing = @($societies | Where-Object { $_.DbName -notin $availableDatabases })
    if ($missing.Count -gt 0) {
        throw "Registered database(s) are missing or offline: $($missing.DbName -join ', ')"
    }

    & docker exec -u root $sqlContainer sh -c "rm -rf '$backupDir' && mkdir -p '$backupDir' && chown mssql:root '$backupDir'"
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the SQL backup directory.' }

    $databaseManifest = @()
    foreach ($society in $societies) {
        $database = [string]$society.DbName
        $fileName = "$database.bak"
        $counts = @(Get-TableCounts -Database $database)
        Write-Host "Backing up $database..." -ForegroundColor Cyan
        Invoke-Sql -Database 'master' -Query "BACKUP DATABASE [$($database.Replace(']', ']]'))] TO DISK = N'$backupDir/$fileName' WITH FORMAT, INIT, COMPRESSION, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'$backupDir/$fileName' WITH CHECKSUM;" | Out-Null
        & docker cp "${sqlContainer}:${backupDir}/$fileName" (Join-Path $stage $fileName)
        if ($LASTEXITCODE -ne 0) { throw "Could not copy backup for $database from the SQL container." }

        $backupPath = Join-Path $stage $fileName
        $databaseManifest += [ordered]@{
            name      = $database
            file      = $fileName
            sha256    = (Get-FileHash -Algorithm SHA256 $backupPath).Hash.ToLowerInvariant()
            sizeBytes = (Get-Item $backupPath).Length
            counts    = $counts
        }
    }

    $manifest = [ordered]@{
        schemaVersion      = 1
        createdUtc         = (Get-Date).ToUniversalTime().ToString('o')
        sourceCommit       = (& git rev-parse HEAD).Trim()
        containsPii        = $true
        includesUploads    = $false
        controlPlanePolicy = 'Operational society metadata only; platform users, billing, support, audit logs, and onboarding contact PII are excluded.'
        societies          = @($societies)
        databases          = @($databaseManifest)
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $stage 'manifest.json')

    if (Test-Path $plainArchive) { Remove-Item -Force $plainArchive }
    & tar -cf $plainArchive -C $stage .
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the plaintext transfer archive.' }

    Write-Host 'Encrypting bundle. Enter a strong, unique passphrase directly into the age prompt.' -ForegroundColor Yellow
    & age --passphrase --output $encryptedBundle $plainArchive
    if ($LASTEXITCODE -ne 0) { throw 'age encryption failed.' }

    $bundleHash = (Get-FileHash -Algorithm SHA256 $encryptedBundle).Hash.ToLowerInvariant()
    Set-Content -Encoding ascii "$encryptedBundle.sha256" "$bundleHash  $([IO.Path]::GetFileName($encryptedBundle))"

    Write-Host "Encrypted export created: $encryptedBundle" -ForegroundColor Green
    Write-Host "SHA-256: $bundleHash"
    Write-Host 'Transfer only the .age and .sha256 files. Send the passphrase through a different channel.'
}
finally {
    if ($apiWasStopped) { & docker compose up -d smms-api smms-ui 2>$null }
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
    Remove-Item -Force $plainArchive -ErrorAction SilentlyContinue
    & docker exec -u root $sqlContainer rm -rf $backupDir 2>$null
    if (Test-Path $stage) { Write-Warning "Plaintext staging directory could not be removed: $stage" }
    if (Test-Path $plainArchive) { Write-Warning "Plaintext archive could not be removed: $plainArchive" }
}
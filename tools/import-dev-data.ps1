[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BundlePath,

    [switch]$ReplaceExisting,

    [switch]$AllowCommitMismatch,

    [switch]$AcknowledgePii
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $AcknowledgePii) {
    throw 'This import contains resident PII, financial records, and password hashes. Re-run with -AcknowledgePii only on an authorized developer machine.'
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

$bundle = [IO.Path]::GetFullPath($BundlePath)
if (-not (Test-Path -LiteralPath $bundle -PathType Leaf)) { throw "Bundle not found: $bundle" }

$sqlContainer = 'sqlserver'
$sqlcmd = '/opt/mssql-tools18/bin/sqlcmd'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$containerBackupDir = "/var/opt/mssql/dev-import-$stamp"
$containerRollbackDir = "/var/opt/mssql/dev-import-rollback-$stamp"
$stage = Join-Path ([IO.Path]::GetTempPath()) "smms-dev-import-$stamp"
$plainArchive = Join-Path ([IO.Path]::GetTempPath()) "smms-dev-import-$stamp.tar"
$apiWasStopped = $false
$changesStarted = $false
$importSucceeded = $false
$rollbackSucceeded = $false
$suppressServiceRestart = $false
$newDatabaseNames = @()
$rollbackDatabaseNames = @()

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
    if ($LASTEXITCODE -ne 0) { throw "SQL command failed against $Database." }
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

function Get-BackupDatabaseName([string]$BackupFile) {
    $query = "RESTORE HEADERONLY FROM DISK = N'$BackupFile';"
    $arguments = @(
        'exec', $sqlContainer, 'sh', '-c',
        'SQLCMDPASSWORD="$SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd "$@"', 'sh',
        '-S', 'localhost', '-U', 'sa', '-C', '-b', '-d', 'master', '-W', '-w', '65535', '-s', '|', '-Q', $query
    )
    $output = @(& docker @arguments)
    if ($LASTEXITCODE -ne 0) { throw "RESTORE HEADERONLY failed for $BackupFile." }

    for ($index = 0; $index -lt $output.Count; $index++) {
        $columns = @($output[$index].Split('|') | ForEach-Object { $_.Trim() })
        $databaseIndex = [Array]::IndexOf([string[]]$columns, 'DatabaseName')
        if ($databaseIndex -ge 0 -and $output.Count -gt ($index + 2)) {
            $values = @($output[$index + 2].Split('|') | ForEach-Object { $_.Trim() })
            if ($values.Count -gt $databaseIndex) { return $values[$databaseIndex] }
        }
    }
    throw "Could not read DatabaseName from RESTORE HEADERONLY for $BackupFile."
}

function ConvertTo-SqlString($Value) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return 'NULL' }
    return "N'$(([string]$Value).Replace("'", "''"))'"
}

function ConvertTo-SqlDate($Value) {
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return 'NULL' }
    return "CONVERT(datetime2, N'$(([string]$Value).Replace("'", "''"))', 127)"
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
    return @((Invoke-Sql -Database $Database -Query $query -Raw) | ConvertFrom-Json)
}

function Assert-Counts($Expected, $Actual, [string]$Database) {
    $expectedMap = @{}
    foreach ($item in @($Expected)) { $expectedMap[[string]$item.TableName] = [long]$item.RowCount }
    foreach ($item in @($Actual)) {
        $table = [string]$item.TableName
        if (-not $expectedMap.ContainsKey($table) -or $expectedMap[$table] -ne [long]$item.RowCount) {
            throw "Row-count validation failed for $Database.$table. Expected $($expectedMap[$table]); found $($item.RowCount)."
        }
    }
}

New-Item -ItemType Directory -Force -Path $stage | Out-Null

try {
    $sidecar = "$bundle.sha256"
    if (Test-Path -LiteralPath $sidecar) {
        $expectedBundleHash = ((Get-Content -LiteralPath $sidecar -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
        $actualBundleHash = (Get-FileHash -Algorithm SHA256 $bundle).Hash.ToLowerInvariant()
        if ($expectedBundleHash -ne $actualBundleHash) { throw 'Encrypted bundle SHA-256 does not match its sidecar file.' }
        Write-Host 'Encrypted bundle SHA-256 verified.' -ForegroundColor Green
    }
    else {
        Write-Warning 'No outer .sha256 sidecar was found. Authenticated age decryption and inner backup hashes will still be verified.'
    }

    Write-Host 'Decrypting bundle. Enter the passphrase directly into the age prompt.' -ForegroundColor Yellow
    & age --decrypt --output $plainArchive $bundle
    if ($LASTEXITCODE -ne 0) { throw 'age decryption failed.' }

    $archiveEntries = @(& tar -tf $plainArchive)
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the decrypted transfer archive.' }
    foreach ($entry in $archiveEntries) {
        if ($entry -match '^[\\/]' -or $entry -match '^[A-Za-z]:' -or $entry -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Unsafe path in transfer archive: $entry"
        }
    }

    & tar -xf $plainArchive -C $stage
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the decrypted transfer archive.' }

    $manifestPath = Join-Path $stage 'manifest.json'
    if (-not (Test-Path $manifestPath)) { throw 'Bundle does not contain manifest.json.' }
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported manifest schema version: $($manifest.schemaVersion)" }
    if (-not [bool]$manifest.containsPii) { throw 'Manifest classification is invalid; expected containsPii=true.' }
    if ([bool]$manifest.includesUploads) { throw 'This database workflow does not import shared uploads. Create a database-only export.' }

    $manifestDatabases = @($manifest.databases)
    $manifestSocieties = @($manifest.societies)
    if ($manifestDatabases.Count -eq 0 -or $manifestSocieties.Count -eq 0) { throw 'Manifest must contain at least one database and society.' }
    if (@($manifestDatabases.name | Sort-Object -Unique).Count -ne $manifestDatabases.Count) { throw 'Manifest contains duplicate database names.' }
    if (@($manifestSocieties.Key | Sort-Object -Unique).Count -ne $manifestSocieties.Count) { throw 'Manifest contains duplicate society keys.' }

    $mappedDatabases = @($manifestSocieties.DbName | Sort-Object)
    $backupDatabases = @($manifestDatabases.name | Sort-Object)
    if (Compare-Object $mappedDatabases $backupDatabases) { throw 'Every manifest society must map to exactly one included database, and vice versa.' }

    $targetCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the target Git commit.' }
    if (-not $AllowCommitMismatch -and [string]$manifest.sourceCommit -ne $targetCommit) {
        throw "Source commit $($manifest.sourceCommit) does not match target commit $targetCommit. Update the target checkout or explicitly use -AllowCommitMismatch."
    }

    foreach ($database in $manifestDatabases) {
        if ([string]$database.name -notmatch '^SmmsDb_[A-Za-z0-9-]+$') { throw "Unsafe database name in manifest: $($database.name)" }
        if ([string]$database.file -ne "$($database.name).bak") { throw "Unexpected backup filename for $($database.name)." }
        $backupPath = Join-Path $stage ([string]$database.file)
        if (-not (Test-Path $backupPath)) { throw "Backup missing from bundle: $($database.file)" }
        $actualHash = (Get-FileHash -Algorithm SHA256 $backupPath).Hash.ToLowerInvariant()
        if ($actualHash -ne ([string]$database.sha256).ToLowerInvariant()) { throw "Backup SHA-256 mismatch: $($database.file)" }
    }
    Write-Host 'All inner backup SHA-256 hashes verified.' -ForegroundColor Green

    & docker compose up -d sqlserver
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the local SQL Server container.' }

    $deadline = (Get-Date).AddMinutes(2)
    $sqlReady = $false
    do {
        & docker exec $sqlContainer sh -c 'SQLCMDPASSWORD="$SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -Q "SELECT 1"' *> $null
        if ($LASTEXITCODE -eq 0) { $sqlReady = $true; break }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    if (-not $sqlReady) { throw 'SQL Server did not become ready.' }

    & docker compose stop smms-api
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop smms-api; import aborted before inventory or database changes.' }
    $apiWasStopped = $true

    $existingDatabases = @(Invoke-Sql -Database 'master' -Query "SELECT name FROM sys.databases WHERE name LIKE 'SmmsDb[_]%';")
    $collisions = @($manifestDatabases | Where-Object { $_.name -in $existingDatabases })
    if ($collisions.Count -gt 0 -and -not $ReplaceExisting) {
        throw "Target database(s) already exist: $($collisions.name -join ', '). Re-run with -ReplaceExisting to create temporary SQL rollback backups and replace them."
    }

    foreach ($society in $manifestSocieties) {
        if ([string]$society.Key -notmatch '^[a-z0-9-]+$') { throw "Unsafe society key in manifest: $($society.Key)" }
        if ([string]$society.DbName -notmatch '^SmmsDb_[A-Za-z0-9-]+$') { throw "Unsafe society database mapping: $($society.DbName)" }
    }

    $targetSocietyExpression = "SELECT [Key], DbName FROM dbo.Societies FOR JSON PATH"
    $targetSocieties = @((Invoke-Sql -Database 'SmmsControlDb' -Query $targetSocietyExpression -Raw) | ConvertFrom-Json)
    foreach ($society in $manifestSocieties) {
        $conflict = @($targetSocieties | Where-Object { $_.DbName -eq $society.DbName -and $_.Key -ne $society.Key })
        if ($conflict.Count -gt 0) { throw "Target society '$($conflict[0].Key)' already maps to imported database '$($society.DbName)'." }
        $remap = @($targetSocieties | Where-Object { $_.Key -eq $society.Key -and $_.DbName -ne $society.DbName })
        if ($remap.Count -gt 0) { throw "Target society '$($society.Key)' maps to '$($remap[0].DbName)', not imported database '$($society.DbName)'. Society remapping is not automatic." }
    }

    $newDatabaseNames = @($manifestDatabases.name | Where-Object { $_ -notin $existingDatabases })

    & docker exec -u root $sqlContainer sh -c "rm -rf '$containerBackupDir' '$containerRollbackDir' && mkdir -p '$containerBackupDir' '$containerRollbackDir' && chown mssql:root '$containerBackupDir' '$containerRollbackDir' && chmod 750 '$containerBackupDir' '$containerRollbackDir'"
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the SQL import and rollback directories.' }

    foreach ($database in $manifestDatabases) {
        $backupPath = Join-Path $stage ([string]$database.file)
        & docker cp $backupPath "${sqlContainer}:${containerBackupDir}/$($database.file)"
        if ($LASTEXITCODE -ne 0) { throw "Could not copy $($database.file) into SQL Server." }
    }
    & docker exec -u root $sqlContainer sh -c "chown -R mssql:root '$containerBackupDir' && find '$containerBackupDir' -type f -exec chmod 640 {} +"
    if ($LASTEXITCODE -ne 0) { throw 'Could not secure SQL import file permissions.' }

    foreach ($database in $manifestDatabases) {
        $backupFile = "$containerBackupDir/$($database.file)"
        $backupDatabaseName = Get-BackupDatabaseName -BackupFile $backupFile
        if ($backupDatabaseName -ne [string]$database.name) {
            throw "Backup identity mismatch for $($database.file). Header says '$backupDatabaseName'; manifest says '$($database.name)'."
        }
        Invoke-Sql -Database 'master' -Query "RESTORE VERIFYONLY FROM DISK = N'$backupFile' WITH CHECKSUM;" | Out-Null
    }

    $rollbackDatabaseNames = @($collisions.name) + @('SmmsControlDb')
    foreach ($name in $rollbackDatabaseNames) {
        if ([string]$name -notmatch '^(SmmsControlDb|SmmsDb_[A-Za-z0-9-]+)$') { throw "Unsafe rollback database name: $name" }
        $rollbackFile = "$containerRollbackDir/$name.bak"
        Invoke-Sql -Database 'master' -Query "BACKUP DATABASE [$($name.Replace(']', ']]'))] TO DISK = N'$rollbackFile' WITH FORMAT, INIT, COMPRESSION, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'$rollbackFile' WITH CHECKSUM;" | Out-Null
    }
    $changesStarted = $true

    foreach ($database in $manifestDatabases) {
        $name = [string]$database.name
        $quotedName = $name.Replace(']', ']]')
        $backupFile = "$containerBackupDir/$($database.file)"
        Write-Host "Verifying and restoring $name..." -ForegroundColor Cyan
        Invoke-Sql -Database 'master' -Query "RESTORE VERIFYONLY FROM DISK = N'$backupFile' WITH CHECKSUM; IF DB_ID(N'$($name.Replace("'", "''"))') IS NOT NULL ALTER DATABASE [$quotedName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$quotedName] FROM DISK = N'$backupFile' WITH REPLACE, RECOVERY; ALTER DATABASE [$quotedName] SET MULTI_USER;" | Out-Null
    }

    foreach ($database in $manifestDatabases) {
        $state = @(Invoke-Sql -Database 'master' -Query "SELECT state_desc FROM sys.databases WHERE name = N'$(([string]$database.name).Replace("'", "''"))';")[0]
        if ($state -ne 'ONLINE') { throw "Database $($database.name) is not ONLINE after restore." }
        Assert-Counts -Expected $database.counts -Actual (Get-TableCounts -Database $database.name) -Database $database.name
    }

    foreach ($society in $manifestSocieties) {
        $key = ConvertTo-SqlString $society.Key
        $displayName = ConvertTo-SqlString $society.DisplayName
        $dbName = ConvertTo-SqlString $society.DbName
        $status = ConvertTo-SqlString $society.Status
        $plan = ConvertTo-SqlString $society.Plan
        $expiryDate = ConvertTo-SqlDate $society.ExpiryDate
        $createdAt = ConvertTo-SqlDate $society.CreatedAt
        $flatCount = [int]$society.FlatCount
        $isDemo = if ([bool]$society.IsDemo) { 1 } else { 0 }
        $upsert = @"
MERGE dbo.Societies WITH (HOLDLOCK) AS target
USING (SELECT $key AS [Key]) AS source
ON target.[Key] = source.[Key]
WHEN MATCHED THEN UPDATE SET
    DisplayName = $displayName, DbName = $dbName, [Status] = $status,
    [Plan] = $plan, ExpiryDate = $expiryDate, FlatCount = $flatCount, IsDemo = $isDemo
WHEN NOT MATCHED THEN INSERT
    ([Key], DisplayName, DbName, [Status], [Plan], ExpiryDate, FlatCount, IsDemo, CreatedAt)
VALUES
    ($key, $displayName, $dbName, $status, $plan, $expiryDate, $flatCount, $isDemo, COALESCE($createdAt, SYSUTCDATETIME()));
"@
        Invoke-Sql -Database 'SmmsControlDb' -Query $upsert | Out-Null
    }

    foreach ($society in $manifestSocieties) {
        $actualDbName = @(Invoke-Sql -Database 'SmmsControlDb' -Query "SELECT DbName FROM dbo.Societies WHERE [Key] = N'$(([string]$society.Key).Replace("'", "''"))';")[0]
        if ($actualDbName -ne [string]$society.DbName) { throw "Control-plane mapping validation failed for society '$($society.Key)'." }
    }

    & docker compose up -d smms-api smms-ui
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the SMMS API and UI.' }

    $healthUrl = 'http://admin.localtest.me:8080/api/health'
    $healthDeadline = (Get-Date).AddMinutes(3)
    $health = $null
    do {
        try {
            $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 5
            if ($health.status -eq 'Healthy') { break }
        }
        catch { }
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $healthDeadline)
    if ($null -eq $health -or $health.status -ne 'Healthy') { throw "API did not become healthy at $healthUrl." }
    $apiWasStopped = $false

    Write-Host 'Import completed and validated.' -ForegroundColor Green
    Write-Host "Source commit: $($manifest.sourceCommit)"
    Write-Host "Societies: $((@($manifest.societies).Key) -join ', ')"
    Write-Host 'The VM-local platform super-admin was preserved.'
    $importSucceeded = $true
}
catch {
    $importError = $_
    if ($changesStarted -and -not $importSucceeded) {
        Write-Warning 'Import failed after database changes began. Restoring the target SQL rollback journal.'
        try {
            & docker compose stop smms-api
            if ($LASTEXITCODE -ne 0) { throw 'Could not stop smms-api before rollback.' }
            foreach ($name in $rollbackDatabaseNames) {
                $quotedName = ([string]$name).Replace(']', ']]')
                $escapedName = ([string]$name).Replace("'", "''")
                $rollbackFile = "$containerRollbackDir/$name.bak"
                Invoke-Sql -Database 'master' -Query "IF DB_ID(N'$escapedName') IS NOT NULL ALTER DATABASE [$quotedName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$quotedName] FROM DISK = N'$rollbackFile' WITH REPLACE, RECOVERY; ALTER DATABASE [$quotedName] SET MULTI_USER;" | Out-Null
            }
            foreach ($name in $newDatabaseNames) {
                $quotedName = ([string]$name).Replace(']', ']]')
                $escapedName = ([string]$name).Replace("'", "''")
                Invoke-Sql -Database 'master' -Query "IF DB_ID(N'$escapedName') IS NOT NULL BEGIN ALTER DATABASE [$quotedName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$quotedName]; END;" | Out-Null
            }
            $rollbackSucceeded = $true
            Write-Warning 'Target databases and control-plane records were restored to their pre-import state.'
        }
        catch {
            $suppressServiceRestart = $true
            throw "Import failed: $($importError.Exception.Message) Automatic rollback also failed: $($_.Exception.Message). Rollback files remain in container '$sqlContainer' at '$containerRollbackDir'."
        }
    }
    throw $importError
}
finally {
    if ($apiWasStopped -and -not $suppressServiceRestart) { & docker compose up -d smms-api smms-ui 2>$null }
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
    Remove-Item -Force $plainArchive -ErrorAction SilentlyContinue
    & docker exec -u root $sqlContainer rm -rf $containerBackupDir 2>$null
    if ($importSucceeded -or $rollbackSucceeded) { & docker exec -u root $sqlContainer rm -rf $containerRollbackDir 2>$null }
    if (Test-Path $stage) { Write-Warning "Plaintext staging directory could not be removed: $stage" }
    if (Test-Path $plainArchive) { Write-Warning "Plaintext archive could not be removed: $plainArchive" }
}
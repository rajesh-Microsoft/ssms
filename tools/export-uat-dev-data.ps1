[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [string]$SocietyKey,

    [string]$Vm = 'ssmsadmin@135.235.195.132',

    [string]$KeyPath = "$env:USERPROFILE\ssmsadmin.pem",

    [ValidateSet('Ssh', 'RunCommand')]
    [string]$Transport = 'Ssh',

    [switch]$AcknowledgePii
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $AcknowledgePii) {
    throw 'This export contains UAT resident PII, financial records, and password hashes. Re-run with -AcknowledgePii only when the recipient is authorized.'
}
if ($SocietyKey -notmatch '^[a-z0-9-]+$') {
    throw "Invalid society key '$SocietyKey'."
}
if (-not (Test-Path -LiteralPath $KeyPath -PathType Leaf)) {
    throw "SSH key not found: $KeyPath"
}
$requiredCommands = @('age', 'tar')
if ($Transport -eq 'Ssh') { $requiredCommands += @('ssh', 'scp') }
else { $requiredCommands += 'az' }
foreach ($command in $requiredCommands) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command is required but was not found."
    }
}

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path ([IO.Path]::GetTempPath()) "smms-uat-export-$stamp"
$plainArchive = Join-Path ([IO.Path]::GetTempPath()) "smms-uat-data-$stamp.tar"
$encryptedBundle = Join-Path $outputRoot "smms-uat-data-$SocietyKey-$stamp.tar.age"
$remoteStage = "/tmp/smms-uat-export-$stamp"
$remoteArchive = "/tmp/smms-uat-data-$stamp.tar"
$remoteScriptPath = "/tmp/smms-uat-export-$stamp.sh"
$localRemoteScript = Join-Path ([IO.Path]::GetTempPath()) "smms-uat-export-$stamp.sh"
$remotePrepared = $false
$sourceSubscription = '35fafe5f-7621-4ee4-8bae-c2accf4fec38'
$sourceResourceGroup = 'ssms-prod-rg'
$sourceVm = 'ssms-webserver'
$transferToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$remoteEncryptedPath = "/home/ssmsadmin/smms/SMMS/UI/dev-transfer-$transferToken.cms"
$remoteCertificatePath = "/tmp/smms-uat-export-$stamp-cert.pem"
$encryptedTransfer = Join-Path ([IO.Path]::GetTempPath()) "smms-uat-transfer-$stamp.cms"
$cmsCertificate = Join-Path ([IO.Path]::GetTempPath()) "smms-uat-transfer-$stamp.crt"
$cmsPrivateKey = Join-Path ([IO.Path]::GetTempPath()) "smms-uat-transfer-$stamp.key"
$opensslPath = 'C:\Program Files\Git\usr\bin\openssl.exe'

if ($Transport -eq 'RunCommand' -and -not (Test-Path -LiteralPath $opensslPath -PathType Leaf)) {
    throw "OpenSSL was not found at the Git for Windows path: $opensslPath"
}

New-Item -ItemType Directory -Force -Path $outputRoot, $stage | Out-Null

$remoteScript = @'
#!/usr/bin/env bash
set -euo pipefail

SOCIETY_KEY='__SOCIETY_KEY__'
STAGE='__REMOTE_STAGE__'
ARCHIVE='__REMOTE_ARCHIVE__'
BACKUP_DIR='/var/opt/mssql/__BACKUP_DIR__'
STACK_ROOT='/home/ssmsadmin/smms/SMMS'
API_WAS_RUNNING=false

cleanup_on_exit() {
  status=$?
  trap - EXIT
  if [ "$API_WAS_RUNNING" = true ]; then
    cd "$STACK_ROOT"
    docker compose up -d smms-api smms-ui >/dev/null 2>&1 || true
  fi
  if [ "$status" -ne 0 ]; then
    rm -rf "$STAGE" "$ARCHIVE"
    docker exec -u root sqlserver rm -rf "$BACKUP_DIR" >/dev/null 2>&1 || true
  fi
  exit "$status"
}
trap cleanup_on_exit EXIT

cd "$STACK_ROOT"
CONNECTION=$(docker exec smms-api printenv ControlPlane__TenantConnectionTemplate)
SA_PASSWORD=$(printf '%s' "$CONNECTION" | sed -n 's/.*Password=\([^;]*\).*/\1/p')
if [ -z "$SA_PASSWORD" ]; then
  echo 'Could not obtain the SQL credential from the UAT API container.' >&2
  exit 1
fi

if [ "$(docker inspect -f '{{.State.Running}}' smms-api 2>/dev/null || true)" = true ]; then
  API_WAS_RUNNING=true
fi
docker compose stop smms-api >/dev/null

sql() {
  docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -b "$@"
}

DB_NAME=$(sql -d SmmsControlDb -h -1 -W -Q "SET NOCOUNT ON; SELECT DbName FROM dbo.Societies WHERE [Key] = N'$SOCIETY_KEY' AND [Status] <> N'Pending';" | tr -d '\r' | xargs)
if [[ ! "$DB_NAME" =~ ^SmmsDb_[A-Za-z0-9-]+$ ]]; then
  echo "No exportable UAT society found for key '$SOCIETY_KEY'." >&2
  exit 1
fi

rm -rf "$STAGE" "$ARCHIVE"
mkdir -p "$STAGE"
docker exec -u root sqlserver sh -c "rm -rf '$BACKUP_DIR' && mkdir -p '$BACKUP_DIR' && chown mssql:root '$BACKUP_DIR'"

sql -d SmmsControlDb -w 65535 -y 0 -Q "SET NOCOUNT ON; SELECT [Key], DisplayName, DbName, [Status], [Plan], ExpiryDate, FlatCount, IsDemo, CreatedAt FROM dbo.Societies WHERE [Key] = N'$SOCIETY_KEY' FOR JSON PATH, INCLUDE_NULL_VALUES;" \
  | tr -d '\r' > "$STAGE/society.json"

sql -d "$DB_NAME" -w 65535 -y 0 -Q "SET NOCOUNT ON; SELECT [TableName], [RowCount] FROM (SELECT 'Users' AS [TableName], COUNT_BIG(*) AS [RowCount] FROM dbo.Users UNION ALL SELECT 'Members', COUNT_BIG(*) FROM dbo.Members UNION ALL SELECT 'Collections', COUNT_BIG(*) FROM dbo.Collections UNION ALL SELECT 'Expenses', COUNT_BIG(*) FROM dbo.Expenses) counts ORDER BY [TableName] FOR JSON PATH;" \
  | tr -d '\r' > "$STAGE/counts.json"

BACKUP_FILE="$BACKUP_DIR/$DB_NAME.bak"
sql -d master -Q "BACKUP DATABASE [$DB_NAME] TO DISK = N'$BACKUP_FILE' WITH FORMAT, INIT, COMPRESSION, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'$BACKUP_FILE' WITH CHECKSUM;" >/dev/null
docker cp "sqlserver:$BACKUP_FILE" "$STAGE/$DB_NAME.bak" >/dev/null

sha256sum "$STAGE/$DB_NAME.bak" > "$STAGE/backup.sha256"
printf '%s\n' "$DB_NAME" > "$STAGE/database-name.txt"
if [ -s DEPLOYED_COMMIT ]; then
  tr -d '\r\n' < DEPLOYED_COMMIT > "$STAGE/source-commit.txt"
else
  git rev-parse HEAD | tr -d '\r\n' > "$STAGE/source-commit.txt"
fi

tar -cf "$ARCHIVE" -C "$STAGE" .
docker exec -u root sqlserver rm -rf "$BACKUP_DIR"
echo "Prepared verified UAT backup for $SOCIETY_KEY ($DB_NAME)."
'@

$remoteScript = $remoteScript.Replace('__SOCIETY_KEY__', $SocietyKey)
$remoteScript = $remoteScript.Replace('__REMOTE_STAGE__', $remoteStage)
$remoteScript = $remoteScript.Replace('__REMOTE_ARCHIVE__', $remoteArchive)
$remoteScript = $remoteScript.Replace('__BACKUP_DIR__', "dev-export-$stamp")
[IO.File]::WriteAllText($localRemoteScript, ($remoteScript -replace "`r", ''), [Text.UTF8Encoding]::new($false))

try {
    Write-Host "Preparing UAT backup for $SocietyKey. The UAT API will pause briefly." -ForegroundColor Cyan
    if ($Transport -eq 'Ssh') {
        & scp -q -i $KeyPath $localRemoteScript "${Vm}:$remoteScriptPath"
        if ($LASTEXITCODE -ne 0) { throw 'Could not copy the UAT export helper to the VM. Request JIT access and retry.' }

        $remoteOutput = @(& ssh -i $KeyPath $Vm "bash '$remoteScriptPath'" 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "UAT backup preparation failed: $($remoteOutput -join [Environment]::NewLine)"
        }
        $remoteOutput | ForEach-Object { Write-Host $_ }
        $remotePrepared = $true

        & scp -q -i $KeyPath "${Vm}:$remoteArchive" $plainArchive
        if ($LASTEXITCODE -ne 0) { throw 'Could not copy the verified UAT backup archive to this workstation.' }
    }
    else {
        & $opensslPath req -x509 -newkey rsa:3072 -keyout $cmsPrivateKey -out $cmsCertificate -days 1 -nodes -subj '/CN=SMMS UAT Transfer' 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Could not create an ephemeral transfer certificate.' }
        $certificateBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($cmsCertificate))

        [IO.File]::AppendAllText($localRemoteScript, "`n", [Text.UTF8Encoding]::new($false))
        Add-Content -Encoding utf8 $localRemoteScript @"
    printf '%s' '$certificateBase64' | base64 -d > '$remoteCertificatePath'
    openssl cms -encrypt -binary -aes-256-cbc -in '$remoteArchive' -out '$remoteEncryptedPath' -outform DER '$remoteCertificatePath'
    chmod 0644 '$remoteEncryptedPath'
    rm -rf '$remoteStage' '$remoteArchive' '$remoteCertificatePath'
    echo 'Prepared encrypted UAT transfer.'
"@

        $raw = @(& az vm run-command invoke --subscription $sourceSubscription -g $sourceResourceGroup -n $sourceVm --command-id RunShellScript --scripts "@$localRemoteScript" -o json 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Azure Run Command failed: $($raw -join [Environment]::NewLine)" }
        $runResult = ($raw -join [Environment]::NewLine) | ConvertFrom-Json
        $remoteOutput = ($runResult.value | ForEach-Object { $_.message }) -join [Environment]::NewLine
        if ($remoteOutput -notmatch 'Prepared encrypted UAT transfer') {
            throw "UAT backup encryption failed: $remoteOutput"
        }
        Write-Host $remoteOutput
        $remotePrepared = $true

        Invoke-WebRequest -Uri "https://135.235.195.132/dev-transfer-$transferToken.cms" -SkipCertificateCheck -OutFile $encryptedTransfer
        & $opensslPath cms -decrypt -binary -inform DER -in $encryptedTransfer -recip $cmsCertificate -inkey $cmsPrivateKey -out $plainArchive 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Could not decrypt the UAT transfer archive locally.' }

        & az vm run-command invoke --subscription $sourceSubscription -g $sourceResourceGroup -n $sourceVm --command-id RunShellScript --scripts "rm -f '$remoteEncryptedPath'" --only-show-errors -o none
        if ($LASTEXITCODE -ne 0) { throw 'Could not remove the encrypted UAT transfer from the VM.' }
    }

    & tar -xf $plainArchive -C $stage
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the UAT transfer archive locally.' }

    $databaseName = (Get-Content (Join-Path $stage 'database-name.txt') -Raw).Trim()
    if ($databaseName -notmatch '^SmmsDb_[A-Za-z0-9-]+$') { throw "Unsafe database name received from UAT: $databaseName" }
    $backupFileName = "$databaseName.bak"
    $backupPath = Join-Path $stage $backupFileName
    if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) { throw "UAT backup is missing: $backupFileName" }

    $remoteHash = ((Get-Content (Join-Path $stage 'backup.sha256') -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $localHash = (Get-FileHash -Algorithm SHA256 $backupPath).Hash.ToLowerInvariant()
    if ($remoteHash -ne $localHash) { throw 'The UAT backup SHA-256 changed during transfer.' }

    $societies = @((Get-Content (Join-Path $stage 'society.json') -Raw) | ConvertFrom-Json)
    $counts = @((Get-Content (Join-Path $stage 'counts.json') -Raw) | ConvertFrom-Json)
    if ($societies.Count -ne 1 -or [string]$societies[0].Key -ne $SocietyKey -or [string]$societies[0].DbName -ne $databaseName) {
        throw 'The UAT society metadata does not match the requested export.'
    }
    if ($counts.Count -ne 4) { throw 'The UAT row-count manifest is incomplete.' }

    $manifest = [ordered]@{
        schemaVersion      = 1
        createdUtc         = (Get-Date).ToUniversalTime().ToString('o')
        sourceCommit       = (Get-Content (Join-Path $stage 'source-commit.txt') -Raw).Trim()
        containsPii        = $true
        includesUploads    = $false
        controlPlanePolicy = 'Operational society metadata only; platform users, billing, support, audit logs, and onboarding contact PII are excluded.'
        societies          = $societies
        databases          = @([ordered]@{
                name      = $databaseName
                file      = $backupFileName
                sha256    = $localHash
                sizeBytes = (Get-Item -LiteralPath $backupPath).Length
                counts    = $counts
            })
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $stage 'manifest.json')

    $bundleInput = Join-Path ([IO.Path]::GetTempPath()) "smms-uat-bundle-$stamp.tar"
    & tar -cf $bundleInput -C $stage $backupFileName 'manifest.json'
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the plaintext UAT bundle.' }

    Write-Host 'Encrypting bundle. Enter and confirm a strong passphrase directly in this terminal.' -ForegroundColor Yellow
    $acknowledgment = Read-Host 'Type READY, then enter a non-empty passphrase at the next prompt'
    if ($acknowledgment -cne 'READY') { throw 'Encryption cancelled. Type READY exactly to continue.' }
    & age --passphrase --output $encryptedBundle $bundleInput
    if ($LASTEXITCODE -ne 0) { throw 'age encryption failed.' }

    $bundleHash = (Get-FileHash -Algorithm SHA256 $encryptedBundle).Hash.ToLowerInvariant()
    Set-Content -Encoding ascii "$encryptedBundle.sha256" "$bundleHash  $([IO.Path]::GetFileName($encryptedBundle))"
    Write-Host "Encrypted UAT export created: $encryptedBundle" -ForegroundColor Green
    Write-Host "SHA-256: $bundleHash"
}
finally {
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
    Remove-Item -Force $plainArchive, $localRemoteScript, $encryptedTransfer, $cmsCertificate, $cmsPrivateKey -ErrorAction SilentlyContinue
    if (Get-Variable bundleInput -ErrorAction SilentlyContinue) {
        Remove-Item -Force $bundleInput -ErrorAction SilentlyContinue
    }
    if ($Transport -eq 'Ssh') {
        $cleanupCommand = "rm -rf '$remoteStage' '$remoteArchive' '$remoteScriptPath'; docker exec -u root sqlserver rm -rf '/var/opt/mssql/dev-export-$stamp' >/dev/null 2>&1 || true"
        & ssh -i $KeyPath $Vm $cleanupCommand 2>$null
        if ($LASTEXITCODE -ne 0 -and $remotePrepared) {
            Write-Warning "Could not confirm UAT staging cleanup. Reconnect and remove $remoteStage and $remoteArchive."
        }
    }
    else {
        $cleanupCommand = "rm -rf '$remoteStage' '$remoteArchive' '$remoteCertificatePath' '$remoteEncryptedPath'; docker exec -u root sqlserver rm -rf '/var/opt/mssql/dev-export-$stamp' >/dev/null 2>&1 || true"
        & az vm run-command invoke --subscription $sourceSubscription -g $sourceResourceGroup -n $sourceVm --command-id RunShellScript --scripts $cleanupCommand --only-show-errors -o none 2>$null
        if ($LASTEXITCODE -ne 0 -and $remotePrepared) { Write-Warning 'Could not confirm encrypted UAT transfer cleanup on the VM.' }
    }
    if (Test-Path $stage) { Write-Warning "Plaintext staging directory could not be removed: $stage" }
    if (Test-Path $plainArchive) { Write-Warning "Plaintext transfer archive could not be removed: $plainArchive" }
}
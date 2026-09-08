<#
.SYNOPSIS
Refreshes ONE society's database on pre-prod from the UAT ("live") box.

.DESCRIPTION
UAT is where the committees actually work, so it is the de-facto production source.
This makes pre-prod's copy of a single society match it, without disturbing the other
societies or the pre-prod control plane.

Order is deliberate: pre-prod is backed up and the backup is VERIFYONLY-proven BEFORE the
source is even read, so there is never a window where the old pre-prod data is gone and no
restorable copy exists.

The source is only ever read (BACKUP DATABASE); UAT is never written to.

.EXAMPLE
  ./deploy/pprod/refresh-pprod-society.ps1 -Society aadya

.EXAMPLE
  ./deploy/pprod/refresh-pprod-society.ps1 -Society aadya -WhatIfOnly
#>
param(
    [string]$Society = 'aadya',
    [switch]$SkipSafetyBackup,
    [switch]$WhatIfOnly,
    [string]$PprodSub = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d',
    [string]$PprodRg = 'smms-pprod-rg',
    [string]$PprodVm = 'smms-pprod-vm',
    [string]$SourceSub = '35fafe5f-7621-4ee4-8bae-c2accf4fec38',
    [string]$SourceRg = 'ssms-prod-rg',
    [string]$SourceVm = 'ssms-webserver'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

if ($Society -notmatch '^[a-z0-9-]+$') { throw "Society key '$Society' is not a valid key." }

function Invoke-OnVm {
    param($Sub, $Rg, $Vm, $ScriptFile, $BlobUrl, $Label, $Expect)

    $body = (Get-Content -Raw (Join-Path $here $ScriptFile)).
    Replace('__SOCIETY__', $Society).
    Replace('__BLOB_URL__', $BlobUrl) -replace "`r", ""
    $tmp = New-TemporaryFile
    [System.IO.File]::WriteAllText($tmp.FullName, $body)

    Write-Host "`n=== $Label ===" -ForegroundColor Cyan
    $raw = az vm run-command invoke --subscription $Sub -g $Rg -n $Vm `
        --command-id RunShellScript --scripts "@$($tmp.FullName)" -o json 2>&1
    Remove-Item $tmp.FullName -ErrorAction SilentlyContinue

    $out = ''
    try { $out = (($raw | ConvertFrom-Json).value | ForEach-Object { $_.message }) -join "`n" }
    catch { $out = "$raw" }
    Write-Output $out

    if ($Expect -and $out -notmatch $Expect) { throw "$Label did not reach '$Expect' - stopping." }
    return $out
}

Write-Host "Refreshing pre-prod society '$Society' from $SourceVm" -ForegroundColor Cyan

if ($WhatIfOnly) {
    Write-Host "`n-WhatIfOnly: reporting both sides, changing nothing." -ForegroundColor Yellow
    Invoke-OnVm $SourceSub $SourceRg $SourceVm 'inspect-society.sh' '' 'SOURCE (UAT)' $null
    Invoke-OnVm $PprodSub  $PprodRg  $PprodVm  'inspect-society.sh' '' 'PRE-PROD' $null
    return
}

$sa = az storage account list -g $PprodRg --subscription $PprodSub --query "[0].name" -o tsv
$key = az storage account keys list -n $sa -g $PprodRg --subscription $PprodSub --query "[0].value" -o tsv
az storage container create -n dbs --account-name $sa --account-key $key --only-show-errors -o none

$expiry = (Get-Date).ToUniversalTime().AddHours(3).ToString('yyyy-MM-ddTHH:mmZ')
$writeSas = az storage container generate-sas --account-name $sa --account-key $key -n dbs --permissions cw --expiry $expiry --https-only -o tsv
$readSas = az storage container generate-sas --account-name $sa --account-key $key -n dbs --permissions r --expiry $expiry --https-only -o tsv
$blobName = "refresh-$Society.tar.gz"
$blob = "https://$sa.blob.core.windows.net/dbs/$blobName"

try {
    if (-not $SkipSafetyBackup) {
        Invoke-OnVm $PprodSub $PprodRg $PprodVm '9b-pprod-safety-backup.sh' '' `
            "PRE-PROD: safety backup of $Society" 'SAFETY_BACKUP_OK'
    }
    else {
        Write-Warning 'Safety backup SKIPPED. The current pre-prod copy will not be recoverable.'
    }

    Invoke-OnVm $SourceSub $SourceRg $SourceVm '9a-source-society-backup.sh' "$blob`?$writeSas" `
        "SOURCE: backup $Society + upload" 'SOURCE_DONE'

    Invoke-OnVm $PprodSub $PprodRg $PprodVm '9c-society-restore.sh' "$blob`?$readSas" `
        "PRE-PROD: restore $Society" 'RESTORE_DONE'
}
finally {
    # The staged tarball holds real resident data; never leave it in blob storage.
    az storage blob delete --account-name $sa --account-key $key -c dbs -n $blobName --only-show-errors -o none 2>$null
    Write-Host "`nStaged blob $blobName removed from $sa." -ForegroundColor DarkGray
}

Write-Host "`nPre-prod '$Society' now matches $SourceVm." -ForegroundColor Green
Write-Host "Compare the SOURCE BASELINE and RESTORED STATE blocks above - they must be identical."

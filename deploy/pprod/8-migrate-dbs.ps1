$ErrorActionPreference = 'Stop'
$pprodSub = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d'
$srcSub = '35fafe5f-7621-4ee4-8bae-c2accf4fec38'
$here = $PSScriptRoot

function Invoke-OnVm {
    param($Sub, $Rg, $Vm, $ScriptFile, $BlobUrl, $Label)
    $body = ((Get-Content -Raw (Join-Path $here $ScriptFile)).Replace('__BLOB_URL__', $BlobUrl)) -replace "`r", ""
    $tmp = New-TemporaryFile
    [System.IO.File]::WriteAllText($tmp.FullName, $body)
    Write-Output "`n=== $Label ==="
    $raw = az vm run-command invoke --subscription $Sub -g $Rg -n $Vm `
        --command-id RunShellScript --scripts "@$($tmp.FullName)" -o json 2>&1
    Remove-Item $tmp.FullName -ErrorAction SilentlyContinue
    try { ($raw | ConvertFrom-Json).value | ForEach-Object { $_.message } } catch { Write-Output $raw }
}

$sa = az storage account list -g smms-pprod-rg --subscription $pprodSub --query "[0].name" -o tsv
$key = az storage account keys list -n $sa -g smms-pprod-rg --subscription $pprodSub --query "[0].value" -o tsv
az storage container create -n dbs --account-name $sa --account-key $key --only-show-errors -o none

$expiry = (Get-Date).ToUniversalTime().AddHours(3).ToString('yyyy-MM-ddTHH:mmZ')
$writeSas = az storage container generate-sas --account-name $sa --account-key $key -n dbs --permissions cw --expiry $expiry --https-only -o tsv
$readSas = az storage container generate-sas --account-name $sa --account-key $key -n dbs --permissions r  --expiry $expiry --https-only -o tsv
$blob = "https://$sa.blob.core.windows.net/dbs/smms-dbs.tar.gz"

Invoke-OnVm $srcSub   'ssms-prod-rg'  'ssms-webserver' '8a-source-backup.sh' "$blob`?$writeSas" 'SOURCE: backup + upload'
Invoke-OnVm $pprodSub 'smms-pprod-rg' 'smms-pprod-vm'  '8b-restore.sh'       "$blob`?$readSas"  'PRE-PROD: download + restore'

az storage blob delete --account-name $sa --account-key $key -c dbs -n smms-dbs.tar.gz --only-show-errors -o none
Write-Output "`nStaged backup blob deleted from $sa."

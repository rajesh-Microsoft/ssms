$ErrorActionPreference = 'Stop'
$sub = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d'
$rg  = 'smms-pprod-rg'
$loc = 'centralindia'

$sa = "smmspprod$(Get-Random -Minimum 100000 -Maximum 999999)"
Write-Output "Storage account: $sa"

az storage account create -n $sa -g $rg -l $loc --sku Standard_LRS --kind StorageV2 `
    --min-tls-version TLS1_2 --allow-blob-public-access false `
    --subscription $sub --only-show-errors -o none

$key = az storage account keys list -n $sa -g $rg --subscription $sub --query "[0].value" -o tsv
az storage container create -n stage --account-name $sa --account-key $key --only-show-errors -o none

# Ship exactly what is committed on HEAD, matching the dev-vm shipping convention.
$tar = Join-Path $env:TEMP 'smms-pprod.tar.gz'
git archive --format=tar.gz -o $tar HEAD
Write-Output "Archive: $tar  ($([math]::Round((Get-Item $tar).Length/1MB,2)) MB)"

az storage blob upload --account-name $sa --account-key $key -c stage `
    -n smms.tar.gz -f $tar --overwrite --only-show-errors -o none

$expiry = (Get-Date).ToUniversalTime().AddHours(2).ToString('yyyy-MM-ddTHH:mmZ')
$sas = az storage blob generate-sas --account-name $sa --account-key $key -c stage `
    -n smms.tar.gz --permissions r --expiry $expiry --https-only -o tsv

$url = "https://$sa.blob.core.windows.net/stage/smms.tar.gz?$sas"
Set-Content -Path (Join-Path $PSScriptRoot 'stage-url.txt') -Value $url -NoNewline
Set-Content -Path (Join-Path $PSScriptRoot 'stage-account.txt') -Value $sa -NoNewline
Write-Output "SAS written to deploy/pprod/stage-url.txt (expires $expiry)"
Remove-Item $tar -ErrorAction SilentlyContinue

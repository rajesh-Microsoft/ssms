$ErrorActionPreference = 'Stop'
$region = 'centralindia'
$sizes = @('Standard_B2as_v2', 'Standard_B4as_v2', 'Standard_D2as_v5', 'Standard_D4as_v5', 'Standard_D2ads_v6', 'Standard_B2ms', 'Standard_B4ms')

foreach ($s in $sizes) {
    $uri = "https://prices.azure.com/api/retail/prices?currencyCode='USD'&`$filter=armRegionName eq '$region' and armSkuName eq '$s' and priceType eq 'Consumption'"
    $r = Invoke-RestMethod $uri
    Write-Output "== $s =="
    $r.Items |
    Select-Object productName, skuName, meterName, @{n = 'Hr'; e = { $_.retailPrice } }, @{n = 'Mo'; e = { [math]::Round($_.retailPrice * 730, 2) } } |
    Sort-Object Hr | Format-Table -AutoSize | Out-String -Width 200 | Write-Output
}

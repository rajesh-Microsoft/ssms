$ErrorActionPreference = 'Stop'
$sub = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d'
$rg  = 'smms-pprod-rg'
$vm  = 'smms-pprod-vm'
$loc = 'centralindia'
$key = "$env:USERPROFILE\.ssh\smms-pprod.pub"

$myip = (Invoke-RestMethod 'https://api.ipify.org?format=json').ip
Write-Output "Admin source IP: $myip"

az vm create `
  --subscription $sub `
  --resource-group $rg `
  --name $vm `
  --location $loc `
  --image Canonical:ubuntu-24_04-lts:server:latest `
  --size Standard_B2as_v2 `
  --admin-username ssmsadmin `
  --ssh-key-values "$key" `
  --os-disk-size-gb 64 `
  --storage-sku StandardSSD_LRS `
  --public-ip-sku Standard `
  --public-ip-address-allocation static `
  --public-ip-address-dns-name "smms-pprod-$(Get-Random -Maximum 99999)" `
  --nsg-rule NONE `
  --only-show-errors -o json | ConvertFrom-Json | Select-Object powerState, publicIpAddress, privateIpAddress, location | Format-List

$nsg = az network nsg list -g $rg --subscription $sub --query "[0].name" -o tsv
Write-Output "NSG: $nsg"

# SSH restricted to the operator IP; HTTP/HTTPS open to the world.
az network nsg rule create --subscription $sub -g $rg --nsg-name $nsg -n allow-ssh-admin `
  --priority 1000 --access Allow --protocol Tcp --direction Inbound `
  --source-address-prefixes $myip --destination-port-ranges 22 --only-show-errors | Out-Null
az network nsg rule create --subscription $sub -g $rg --nsg-name $nsg -n allow-http `
  --priority 1010 --access Allow --protocol Tcp --direction Inbound `
  --source-address-prefixes Internet --destination-port-ranges 80 --only-show-errors | Out-Null
az network nsg rule create --subscription $sub -g $rg --nsg-name $nsg -n allow-https `
  --priority 1020 --access Allow --protocol Tcp --direction Inbound `
  --source-address-prefixes Internet --destination-port-ranges 443 --only-show-errors | Out-Null

Write-Output "`n--- NSG rules ---"
az network nsg rule list --subscription $sub -g $rg --nsg-name $nsg -o json |
  ConvertFrom-Json | Select-Object priority, name, access, direction, protocol,
    @{n='src';e={$_.sourceAddressPrefix}}, @{n='ports';e={$_.destinationPortRange}} |
  Sort-Object priority | Format-Table -AutoSize

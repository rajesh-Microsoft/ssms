$ErrorActionPreference = 'Stop'
$sub = '35fafe5f-7621-4ee4-8bae-c2accf4fec38'
$rg = 'ssms-prod-rg'
$vm = 'ssms-webserver'
$loc = 'centralindia'

$myip = (Invoke-RestMethod 'https://api.ipify.org?format=json').ip
Write-Output "Source IP: $myip"

$body = @{
    virtualMachines = @(
        @{
            id    = "/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Compute/virtualMachines/$vm"
            ports = @(
                @{ number = 22; duration = 'PT3H'; allowedSourceAddressPrefix = $myip }
            )
        }
    )
    justification   = 'Razorpay deployment verification and rollout'
} | ConvertTo-Json -Depth 6

$body | Out-File -FilePath jit.json -Encoding ascii

$uri = "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Security/locations/$loc/jitNetworkAccessPolicies/default/initiate?api-version=2020-01-01"
az rest --method post --uri $uri --body '@jit.json' --only-show-errors | Out-Null
Write-Output "JIT requested."
Remove-Item jit.json -ErrorAction SilentlyContinue

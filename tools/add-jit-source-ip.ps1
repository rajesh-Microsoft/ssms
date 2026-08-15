$ErrorActionPreference = 'Stop'
$sub = '35fafe5f-7621-4ee4-8bae-c2accf4fec38'
$rg = 'ssms-prod-rg'
$vm = 'ssms-webserver'
$loc = 'centralindia'

$myip = (Invoke-RestMethod 'https://api.ipify.org?format=json').ip
Write-Output "Source IP: $myip"

$uri = "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Security/locations/$loc/jitNetworkAccessPolicies/default?api-version=2020-01-01"
$policy = az rest --method get --uri $uri --only-show-errors | ConvertFrom-Json

# Only the allowed-prefix list is touched; requests/history are not sent back on PUT.
$vmEntry = $policy.properties.virtualMachines | Where-Object { $_.id -like "*/virtualMachines/$vm" }
$port = $vmEntry.ports | Where-Object { $_.number -eq 22 }

if ($port.allowedSourceAddressPrefixes -contains $myip) {
    Write-Output "Already allowed. No change."
    exit 0
}

$prefixes = @($port.allowedSourceAddressPrefixes) + $myip

$body = @{
    kind       = $policy.kind
    properties = @{
        virtualMachines = @(
            @{
                id    = $vmEntry.id
                ports = @(
                    @{
                        number                       = 22
                        protocol                     = $port.protocol
                        allowedSourceAddressPrefixes = $prefixes
                        maxRequestAccessDuration     = $port.maxRequestAccessDuration
                    }
                )
            }
        )
    }
} | ConvertTo-Json -Depth 8

$body | Out-File -FilePath jit-policy.json -Encoding ascii
az rest --method put --uri $uri --body '@jit-policy.json' --only-show-errors | Out-Null
Remove-Item jit-policy.json -ErrorAction SilentlyContinue
Write-Output "Added $myip to port 22 allowlist ($($prefixes.Count) prefixes)."

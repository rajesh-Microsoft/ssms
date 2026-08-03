# Local-only sandbox helper: posts a signed synthetic Razorpay webhook so the settlement path
# can be exercised without waiting for a real capture.
param(
    [string]$Url = 'http://ssms.localtest.me:7253/api/webhooks/razorpay',
    [Parameter(Mandatory = $true)][string]$Society,
    [Parameter(Mandatory = $true)][string]$OrderId,
    [string]$PaymentId = "pay_HOOK$((Get-Random -Maximum 999999))",
    [Parameter(Mandatory = $true)][long]$AmountPaise,
    [string]$Currency = 'INR',
    [switch]$TamperSignature
)

$secret = (docker exec smms-api printenv Razorpay__WebhookSecret).Trim()
if (-not $secret) { throw 'Razorpay__WebhookSecret is not set in the smms-api container.' }

$body = @{
    event   = 'payment.captured'
    payload = @{
        payment = @{
            entity = @{
                id       = $PaymentId
                order_id = $OrderId
                amount   = $AmountPaise
                currency = $Currency
                status   = 'captured'
                notes    = @{ society = $Society }
            }
        }
    }
} | ConvertTo-Json -Depth 8 -Compress

$hm = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
$sig = ($hm.ComputeHash([Text.Encoding]::UTF8.GetBytes($body)) | ForEach-Object { $_.ToString('x2') }) -join ''
if ($TamperSignature) { $sig = 'deadbeef' }

try {
    $r = Invoke-RestMethod $Url -Method Post -ContentType 'application/json' `
        -Headers @{ 'X-Razorpay-Signature' = $sig } -Body $body
    "200 $($r | ConvertTo-Json -Compress)  (paymentId $PaymentId)"
}
catch {
    "$($_.Exception.Response.StatusCode.value__): $($_.ErrorDetails.Message -replace '\s+', ' ')"
}

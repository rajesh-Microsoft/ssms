# Local-only sandbox test helper: exercises negative/replay paths of /api/member/razorpay/verify.
# Reads the test key secret from the running smms-api container so nothing is hardcoded.
param(
    [string]$Base = 'http://demo.localtest.me:8080',
    [string]$User = 'res-a1002',
    [string]$Pass = 'demo123',
    [Parameter(Mandatory = $true)][string]$OrderId,
    [Parameter(Mandatory = $true)][string]$PaymentId
)

$secret = (docker exec smms-api printenv Razorpay__KeySecret).Trim()
if (-not $secret) { throw 'Razorpay__KeySecret is not set in the smms-api container.' }

$t = (Invoke-RestMethod "$Base/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ username = $User; password = $Pass } | ConvertTo-Json -Compress)).token
$h = @{ Authorization = "Bearer $t" }

function Get-Signature([string]$o, [string]$p) {
    $hm = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
    ($hm.ComputeHash([Text.Encoding]::UTF8.GetBytes("$o|$p")) | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Invoke-Case([string]$name, [hashtable]$payload) {
    $body = $payload | ConvertTo-Json -Compress
    try {
        $r = Invoke-RestMethod "$Base/api/member/razorpay/verify" -Method Post -Headers $h `
            -ContentType 'application/json' -Body $body
        "$name -> 200 $($r | ConvertTo-Json -Compress)"
    }
    catch {
        "$name -> $($_.Exception.Response.StatusCode.value__): $($_.ErrorDetails.Message -replace '\s+', ' ')"
    }
}

Invoke-Case 'A. replay same payment (valid sig)' @{ orderId = $OrderId; paymentId = $PaymentId; signature = (Get-Signature $OrderId $PaymentId) }
Invoke-Case 'B. tampered signature'              @{ orderId = $OrderId; paymentId = $PaymentId; signature = 'deadbeef' }

$fake = 'pay_FAKE1234567890'
Invoke-Case 'C. different paymentId (valid sig)' @{ orderId = $OrderId; paymentId = $fake; signature = (Get-Signature $OrderId $fake) }
Invoke-Case 'D. unknown order'                   @{ orderId = 'order_DOESNOTEXIST'; paymentId = $PaymentId; signature = (Get-Signature 'order_DOESNOTEXIST' $PaymentId) }

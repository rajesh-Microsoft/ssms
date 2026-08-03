$urls = @(
    @{ n = 'dev tenant SPA      '; m = 'GET';  u = 'https://dev-demo.yuvaansoft.shop/' }
    @{ n = 'dev admin SPA       '; m = 'GET';  u = 'https://dev-admin.yuvaansoft.shop/' }
    @{ n = 'dev portal          '; m = 'GET';  u = 'https://dev-ssms.yuvaansoft.shop/' }
    @{ n = 'DEV webhook (live)  '; m = 'POST'; u = 'https://dev-demo.yuvaansoft.shop/api/webhooks/razorpay' }
    @{ n = 'PROD webhook (off)  '; m = 'POST'; u = 'https://ssms.yuvaansoft.shop/api/webhooks/razorpay' }
    @{ n = 'PROD portal         '; m = 'GET';  u = 'https://ssms.yuvaansoft.shop/' }
    @{ n = 'PROD admin          '; m = 'GET';  u = 'https://admin.yuvaansoft.shop/' }
    @{ n = 'PROD marketing      '; m = 'GET';  u = 'https://www.yuvaansoft.shop/' }
)

foreach ($x in $urls) {
    try {
        $r = Invoke-WebRequest -Uri $x.u -Method $x.m -SkipHttpErrorCheck -TimeoutSec 20 -MaximumRedirection 0
        $code = $r.StatusCode
    }
    catch { $code = "ERR: $($_.Exception.Message)" }
    "{0}  {1,-6} {2}" -f $x.n, $x.m, $code
}

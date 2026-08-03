$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== WHAT PRODUCTION ACTUALLY SERVES (live over HTTPS) ====="
echo -n "prod app.js   razorpay refs : "
curl -s https://aadya.yuvaansoft.shop/app.js | grep -c -i razorpay
echo -n "prod mpay.html razorpay refs: "
curl -s https://aadya.yuvaansoft.shop/partials/mpay.html | grep -c -i razorpay
echo -n "prod app.js   size          : "
curl -s https://aadya.yuvaansoft.shop/app.js | wc -c

echo ""
echo "===== SAME FILES INSIDE THE RUNNING PROD UI CONTAINER ====="
echo -n "container app.js razorpay   : "
docker exec smms-ui grep -c -i razorpay /usr/share/nginx/html/app.js
echo -n "container mpay.html razorpay: "
docker exec smms-ui grep -c -i razorpay /usr/share/nginx/html/partials/mpay.html

echo ""
echo "===== FOR CONTRAST: DEV SERVES ====="
echo -n "dev app.js    razorpay refs : "
curl -s https://dev-demo.yuvaansoft.shop/app.js | grep -c -i razorpay
echo -n "dev mpay.html razorpay refs : "
curl -s https://dev-demo.yuvaansoft.shop/partials/mpay.html | grep -c -i razorpay

echo ""
echo "===== WHAT THE PROD PAYMENT SCREEN OFFERS (markup) ====="
curl -s https://aadya.yuvaansoft.shop/partials/mpay.html | grep -o -i -E 'upi|qr|card|netbanking|proof|screenshot' | sort | uniq -c | sort -rn
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

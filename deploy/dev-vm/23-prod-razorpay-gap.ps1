$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS
echo "===== PROD: Razorpay source files present? ====="
find api/SMMS.Api -iname '*razorpay*' -printf '%p\n' 2>/dev/null
echo "(none above = missing)"
echo ""
echo "===== PROD: Razorpay markers in key files ====="
echo -n "appsettings.json  : "; grep -c -i razorpay api/SMMS.Api/appsettings.json
echo -n "PaymentService.cs : "; grep -c -i razorpay api/SMMS.Api/Services/Payments/PaymentService.cs
echo -n "MemberPayments    : "; grep -c -i razorpay api/SMMS.Api/Controllers/MemberPaymentsController.cs
echo -n "PaymentDtos.cs    : "; grep -c -i razorpay api/SMMS.Api/Dtos/PaymentDtos.cs
echo -n "UI/app.js         : "; grep -c -i razorpay UI/app.js
echo -n "UI/partials/mpay  : "; grep -c -i razorpay UI/partials/mpay.html
echo -n "docker-compose    : "; grep -c -i razorpay docker-compose.yml
echo ""
echo "===== PROD: latest EF migrations on disk ====="
ls -1 api/SMMS.Api/Migrations/ | grep -v Designer | grep -v Snapshot | tail -n 6
echo ""
echo "===== PROD: .env present? ====="
ls -la .env 2>&1 | head -n 2
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

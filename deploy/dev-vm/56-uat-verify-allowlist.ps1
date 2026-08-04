$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
API=http://localhost:7253
HOSTD="Host: demo.yuvaansoft.shop"
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
PW='YourStrong@Passw0rd'

MEMBER=$(docker exec sqlserver $SQLCMD -S localhost -U sa -P "$PW" -C -d SmmsDb_Demo -h -1 -W -Q "SET NOCOUNT ON; SELECT TOP 1 Username FROM Users WHERE Role = 'Member' ORDER BY Id;" | tr -d '\r\n ')
echo "test member: $MEMBER  (password demo123)"

TOKEN=$(curl -s -X POST $API/api/auth/login -H "$HOSTD" -H "Content-Type: application/json" -d "{\"username\":\"$MEMBER\",\"password\":\"demo123\"}" | python3 -c "import sys,json;print(json.load(sys.stdin).get('token',''))")
echo "member token length: ${#TOKEN}"

echo ""
echo "===== A. demo IS on the allowlist ====="
curl -s $API/api/member/payment-options -H "$HOSTD" -H "Authorization: Bearer $TOKEN" ; echo ""

echo ""
echo "===== B. remove demo from the allowlist, same process ====="
cd ~/smms/SMMS
sed -i 's/^RAZORPAY_ALLOWED_SOCIETIES=.*/RAZORPAY_ALLOWED_SOCIETIES=vr/' .env
docker compose up -d smms-api > /dev/null 2>&1 ; echo "restart exit: $?"
sleep 12
curl -s $API/api/member/payment-options -H "$HOSTD" -H "Authorization: Bearer $TOKEN" ; echo ""

echo ""
echo "===== C. restore demo to the allowlist ====="
sed -i 's/^RAZORPAY_ALLOWED_SOCIETIES=.*/RAZORPAY_ALLOWED_SOCIETIES=demo/' .env
docker compose up -d smms-api > /dev/null 2>&1 ; echo "restart exit: $?"
sleep 12
curl -s $API/api/member/payment-options -H "$HOSTD" -H "Authorization: Bearer $TOKEN" ; echo ""
grep RAZORPAY_ALLOWED_SOCIETIES .env

echo ""
echo "===== WEBHOOK NOW ANSWERS (401 not 404) ====="
curl -s -o /dev/null -w "webhook: %{http_code}\n" -X POST $API/api/webhooks/razorpay -H "$HOSTD" -H "Content-Type: application/json" -d '{}'
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

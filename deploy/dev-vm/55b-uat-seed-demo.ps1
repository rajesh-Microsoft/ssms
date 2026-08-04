$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Platform endpoints live on the reserved platform host, not a society subdomain.
$remote = @'
API=http://localhost:7253
HOSTP="Host: www.yuvaansoft.shop"

echo "===== PLATFORM LOGIN ====="
PTOKEN=$(curl -s -X POST $API/api/platform/auth/login -H "$HOSTP" -H "Content-Type: application/json" -d '{"username":"superadmin","password":"super123"}' | python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('token') or d.get('accessToken') or '')")
echo "platform token length: ${#PTOKEN}"

echo ""
echo "===== GENERATE DEMO DATA ====="
curl -s -X POST "$API/api/platform/societies/demo/generate-demo?preset=small" -H "$HOSTP" -H "Authorization: Bearer $PTOKEN" ; echo ""
echo "--- end generate ---"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

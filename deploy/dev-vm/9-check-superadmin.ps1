$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
DEV_SUPER=$(grep '^DEV_SUPERADMIN_PASSWORD=' ~/smms-dev/SMMS/.env | cut -d= -f2-)
PRD_SUPER=$(docker exec smms-api printenv ControlPlane__SuperAdmin__Password)
H='Host: admin.yuvaansoft.shop'
U='http://172.17.0.1:8081/api/platform/auth/login'

try_login() {
  code=$(curl -s -o /tmp/lg.json -w '%{http_code}' -X POST "$U" -H "$H" \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"superadmin\",\"password\":\"$1\"}")
  echo "  $2 -> HTTP $code"
  if [ "$code" = "200" ]; then grep -o '"token":"[^"]*' /tmp/lg.json | head -c 20 > /tmp/tok.flag; fi
}

echo "===== WHICH SUPERADMIN PASSWORD WORKS ON DEV ====="
try_login "$PRD_SUPER" "production password"
try_login "$DEV_SUPER"  "generated dev password"
rm -f /tmp/lg.json /tmp/tok.flag
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

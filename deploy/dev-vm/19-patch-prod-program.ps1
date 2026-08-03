# Surgical production patch: replace the per-tenant `throw;` with a logged give-up
# so one unreachable tenant DB can no longer take all 10 societies down on boot.
# Builds BEFORE stopping anything - the old container keeps serving during the build,
# and the previous image is tagged so a rollback is one command.
$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

scp -i $key .\prod-Program.cs "${vm}:~/Program.cs.new" | Out-Null

$remote = @'
set -e
cd ~/smms/SMMS
SRC=api/SMMS.Api/Program.cs
STAMP=$(date +%Y%m%dT%H%M%SZ)

cp "$SRC" ~/Program.cs.bak-$STAMP
echo "backup: ~/Program.cs.bak-$STAMP"

docker tag smms-smms-api smms-smms-api:rollback-$STAMP
echo "rollback image: smms-smms-api:rollback-$STAMP"

cp ~/Program.cs.new "$SRC"
rm -f ~/Program.cs.new

echo ""
echo "===== VERIFY PATCH APPLIED ====="
grep -n "Giving up" "$SRC" || { echo "PATCH MISSING"; exit 1; }
grep -c "throw;" "$SRC" | sed 's/^/remaining throw; statements (expect 1, the control DB): /'

echo ""
echo "===== BUILD (old container still serving) ====="
docker compose build smms-api 2>&1 | tail -20
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

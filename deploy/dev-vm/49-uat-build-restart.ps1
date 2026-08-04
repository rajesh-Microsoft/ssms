$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Note: never leave a numeric argument at the end of a line here. The CR that
# survives the pipe turns "tail -n 15" into "tail -n 15\r" and it fails.
$remote = @'
cd ~/smms/SMMS
echo "===== BUILD ====="
docker compose build smms-api > /tmp/build.log 2>&1 ; echo "build exit: $?"
tail -n 12 /tmp/build.log ; echo "--- end build tail ---"

echo ""
echo "===== RESTART API ====="
docker compose up -d smms-api > /tmp/up.log 2>&1 ; echo "up exit: $?"
cat /tmp/up.log ; echo "--- end up ---"

echo ""
echo "===== CONTAINERS ====="
docker ps --format '{{.Names}}\t{{.Status}}' ; echo "--- end ps ---"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

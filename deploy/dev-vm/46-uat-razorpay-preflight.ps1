$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Read-only. Establishes the rollback position before touching UAT.
$remote = @'
cd ~/smms/SMMS

echo "===== CURRENT API IMAGE (rollback target) ====="
docker inspect smms-api --format '{{.Config.Image}} {{.Image}}' 2>&1

echo ""
echo "===== CONTAINERS ====="
docker ps --format '{{.Names}}\t{{.Status}}' | grep -v dev

echo ""
echo "===== .env VARIABLE NAMES (values hidden) ====="
cut -d= -f1 .env 2>&1 | grep -v '^#' | grep .

echo ""
echo "===== DOES COMPOSE ALREADY REFERENCE RAZORPAY ====="
grep -c -i razorpay docker-compose.yml

echo ""
echo "===== LATEST BACKUP ====="
ls -1t ~/smms-backups/*.tar.gz 2>&1 | head -n 1

echo ""
echo "===== DISK ====="
df -h / | tail -n 1
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

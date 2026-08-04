$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== LOCAL BACKUP TARBALLS ====="
ls -lah ~/smms-backups/ 2>&1 | tail -n 12

echo ""
echo "===== CRON LINE ====="
crontab -l | grep -i backup

echo ""
echo "===== CAN CRON SUDO WITHOUT A PASSWORD? ====="
sudo -n docker ps --format '{{.Names}}' 2>&1 | head -n 3

echo ""
echo "===== IS AZCOPY INSTALLED ====="
which azcopy 2>&1
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

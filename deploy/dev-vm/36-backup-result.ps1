$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== CRON ====="
crontab -l | grep -i backup
echo ""
echo "===== BACKUP LOG ====="
tail -n 25 /home/ssmsadmin/backup.log 2>&1
echo ""
echo "===== TARBALLS ====="
ls -lah ~/smms-backups/ 2>&1 | tail -n 8
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

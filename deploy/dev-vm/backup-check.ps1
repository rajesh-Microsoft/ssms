$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== BACKUP CRON SCHEDULE ====="
crontab -l | grep -i backup

echo ""
echo "===== DO BACKUPS ACTUALLY EXIST? ====="
ls -lah ~/backups/ 2>&1 | tail -n 15

echo ""
echo "===== BACKUP LOG (last run?) ====="
tail -n 15 ~/backup.log 2>&1

echo ""
echo "===== ANY SQL BACKUPS ON DISK ====="
sudo find / -name '*.bak' -newermt '2026-07-01' -printf '%TY-%Tm-%Td %s %p\n' 2>/dev/null | tail -n 10
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

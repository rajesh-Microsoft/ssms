$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# The VM is deallocated by Azure at ~19:47 UTC, so the 21:00 slot never fired.
# 18:00 UTC (23:30 IST) sits inside the VM's uptime window with ~1h45m of margin.
$remote = @'
echo "===== BEFORE ====="
crontab -l | grep -i backup

crontab -l | sed 's|^0 21 \* \* \* /home/ssmsadmin/nightly-backup.sh|0 18 * * * /home/ssmsadmin/nightly-backup.sh|' > /tmp/ct.new
crontab /tmp/ct.new

echo ""
echo "===== AFTER ====="
crontab -l | grep -i backup

echo ""
echo "===== RUNNING THE JOB ONCE TO PROVE IT WORKS ====="
/home/ssmsadmin/nightly-backup.sh >> /home/ssmsadmin/backup.log 2>&1
echo "exit=$?"
tail -n 15 /home/ssmsadmin/backup.log

echo ""
echo "===== TARBALLS NOW ====="
ls -lah ~/smms-backups/*.tar.gz | tail -n 5
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

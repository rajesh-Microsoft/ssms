$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== WHAT REQUESTED THE POWEROFF (last boot, before 19:47) ====="
sudo journalctl -b -1 --no-pager 2>/dev/null | grep -iE "power off|poweroff|shutdown|scheduled|logind|walinuxagent|Stopping" | grep -iE "19:4[0-9]" | head -n 25

echo ""
echo "===== ROOT CRONTAB ====="
sudo crontab -l 2>&1 | head -n 20
echo "===== USER CRONTAB ====="
crontab -l 2>&1 | head -n 20
echo "===== /etc/crontab + cron.d ====="
grep -rhvE '^\s*#|^\s*$' /etc/crontab /etc/cron.d/ 2>/dev/null | head -n 20

echo ""
echo "===== SYSTEMD TIMERS ====="
systemctl list-timers --all --no-pager 2>/dev/null | head -n 12

echo ""
echo "===== ANY SHUTDOWN UNIT / SCRIPT ====="
ls -1 /etc/cron.daily/ 2>/dev/null
grep -rl 'poweroff\|shutdown -h\|deallocate' /etc/cron* /usr/local/bin 2>/dev/null | head -n 10
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

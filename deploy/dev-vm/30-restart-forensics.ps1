$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== ALL CONTAINERS ====="
docker ps -a --format 'table {{.Names}}\t{{.Status}}\t{{.RunningFor}}'

echo ""
echo "===== RESTART COUNTS / EXIT CODES ====="
docker inspect sqlserver --format 'sqlserver     restarts={{.RestartCount}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} started={{.State.StartedAt}}'
docker inspect smms-dev-sql --format 'smms-dev-sql  restarts={{.RestartCount}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} started={{.State.StartedAt}}'
docker inspect smms-api --format 'smms-api      restarts={{.RestartCount}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} started={{.State.StartedAt}}'

echo ""
echo "===== HOST UPTIME / RECENT REBOOT? ====="
uptime
last reboot | head -n 3

echo ""
echo "===== KERNEL OOM KILLS? ====="
sudo dmesg -T 2>/dev/null | grep -i -E 'out of memory|killed process' | tail -n 10
echo "(nothing above = no OOM kills in dmesg)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

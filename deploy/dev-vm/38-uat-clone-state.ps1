$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== UAT CLONE: CURRENT COMMIT ====="
git -C ~/smms/SMMS log --oneline -1 2>&1

echo ""
echo "===== UNCOMMITTED LOCAL CHANGES (would be lost by reset --hard) ====="
git -C ~/smms/SMMS status --short 2>&1 | head -n 25

echo ""
echo "===== IS THE Program.cs PATCH COMMITTED OR LOOSE? ====="
grep -n "stays unavailable until its database is reachable" ~/smms/SMMS/api/SMMS.Api/Program.cs 2>&1

echo ""
echo "===== ARE THE .bak FILES PRESENT IN THE UAT CLONE ====="
du -sh ~/smms/SMMS/data/sql-backups ~/smms/SMMS/backups 2>&1
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

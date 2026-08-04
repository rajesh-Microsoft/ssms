$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== DOES THE VM CLONE FROM GITHUB? ====="
ls -d ~/smms/SMMS/.git 2>&1
ls -d ~/smms-dev/SMMS/.git 2>&1
echo ""
echo "===== ANY GIT REMOTES ON THE BOX ====="
git -C ~/smms/SMMS remote -v 2>&1 | head -n 3
git -C ~/smms-dev/SMMS remote -v 2>&1 | head -n 3
echo ""
echo "===== DEPLOYED COMMIT MARKERS ====="
cat ~/smms/SMMS/DEPLOYED_COMMIT 2>&1
cat ~/smms-dev/SMMS/DEPLOYED_COMMIT 2>&1
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

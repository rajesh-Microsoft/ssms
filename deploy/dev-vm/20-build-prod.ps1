$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS
docker compose build smms-api > /tmp/build.log 2>&1
echo "BUILD EXIT CODE: $?"
echo "----- last lines of build log -----"
tail -n 25 /tmp/build.log
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

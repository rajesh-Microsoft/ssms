# Stage 1: ship code + config + generated dev secrets to the VM.
# Safe to re-run: it only writes under ~/smms-dev and never touches ~/smms.
$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

function New-Secret([int]$bytes) {
    $b = [byte[]]::new($bytes)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($b)
    [Convert]::ToBase64String($b) -replace '[+/=]', ''
}

# Reuse the existing Razorpay TEST credentials; mint everything else fresh so
# no dev secret is shared with production.
$envMap = @{}
Get-Content .\.env | Where-Object { $_ -match '^\s*[A-Z_]+\s*=' } | ForEach-Object {
    $k, $v = $_ -split '=', 2
    $envMap[$k.Trim()] = $v.Trim()
}

$devSa = 'Dv' + (New-Secret 18) + '#7aZ'
$devJwt = New-Secret 48
$devSuper = 'Sa' + (New-Secret 12) + '!9'
$devHook = New-Secret 24

$envText = @"
# Generated for the smms-dev stack. Not in git. Distinct from production.
DEV_SA_PASSWORD=$devSa
DEV_JWT_KEY=$devJwt
DEV_SUPERADMIN_PASSWORD=$devSuper

RAZORPAY_ENABLED=true
RAZORPAY_KEY_ID=$($envMap['RAZORPAY_KEY_ID'])
RAZORPAY_KEY_SECRET=$($envMap['RAZORPAY_KEY_SECRET'])
RAZORPAY_WEBHOOK_SECRET=$devHook
"@

# LF endings: this file is parsed by Compose on Linux.
$envText = $envText -replace "`r", ""
[IO.File]::WriteAllText("$PWD\dev.env.tmp", $envText)

# Tracked files only at the exact commit under test - no bin/obj, no stray tgz.
$commit = (git rev-parse HEAD).Trim()
git archive --format=tar.gz -o smms-dev-code.tgz HEAD

Write-Output "Shipping commit $commit ..."

ssh -i $key $vm "mkdir -p ~/smms-dev/SMMS/nginx"
scp -i $key smms-dev-code.tgz "${vm}:~/smms-dev/code.tgz"                      | Out-Null
scp -i $key deploy\dev-vm\docker-compose.yml "${vm}:~/smms-dev/compose.yml"    | Out-Null
scp -i $key deploy\dev-vm\nginx.dev-vm.conf "${vm}:~/smms-dev/nginx.conf"      | Out-Null
scp -i $key dev.env.tmp "${vm}:~/smms-dev/dev.env"                             | Out-Null

$remote = @'
set -e
cd ~/smms-dev/SMMS
tar xzf ~/smms-dev/code.tgz
# The repo's own compose file describes PRODUCTION container names. Replacing it
# means a bare 'docker compose up' in this directory can never target prod.
mv ~/smms-dev/compose.yml docker-compose.yml
mv ~/smms-dev/nginx.conf  nginx/nginx.dev-vm.conf
mv ~/smms-dev/dev.env     .env
chmod 600 .env
rm -f ~/smms-dev/code.tgz
echo "__COMMIT__" > DEPLOYED_COMMIT
echo "--- dev tree ready at $(pwd), commit $(cat DEPLOYED_COMMIT)"
docker compose config --quiet && echo "compose config: VALID"
'@
$remote = ($remote -replace '__COMMIT__', $commit) -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

Remove-Item dev.env.tmp, smms-dev-code.tgz -ErrorAction SilentlyContinue
Write-Output "Stage 1 done. Secrets exist only in ~/smms-dev/SMMS/.env on the VM."

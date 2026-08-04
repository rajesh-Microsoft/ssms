$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS
echo "===== local-nuget ownership/permissions ====="
ls -la api/SMMS.Api/local-nuget | head -n 10
echo ""
echo "===== did the rest of the extract land? ====="
ls -la api/SMMS.Api/Services/Payments/ 2>&1
echo ""
ls -la api/SMMS.Api/Controllers/RazorpayWebhookController.cs 2>&1
echo ""
cat DEPLOYED_COMMIT 2>&1
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

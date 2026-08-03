$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS/api/SMMS.Api
echo "===== Files the new Program.cs depends on ====="
ls -1 Services/Control/TenantProvisioningService.cs 2>&1
ls -1 Services/Payments/ 2>&1
echo ""
echo "===== Razorpay setting in prod appsettings.json ====="
grep -A3 '"Razorpay"' appsettings.json
echo ""
echo "===== Current prod API image + container ====="
docker inspect smms-api --format '{{.Config.Image}}  started={{.State.StartedAt}}'
docker images --format '{{.Repository}}:{{.Tag}}  {{.ID}}  {{.CreatedSince}}' | grep -i smms
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

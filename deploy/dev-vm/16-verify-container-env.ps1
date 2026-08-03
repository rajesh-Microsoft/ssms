$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== DEV container env ====="
docker exec smms-dev-api env | grep -i razorpay | sort

echo ""
echo "===== PROD container env (expect empty/false) ====="
docker exec smms-api env | grep -i razorpay | sort
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

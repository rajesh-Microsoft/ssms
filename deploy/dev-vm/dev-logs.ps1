$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
echo "===== DEV API LOG ====="
docker logs smms-dev-api 2>&1 | tail -n 40
echo ""
echo "===== DEV UI -> API THROUGH DEV NGINX (Host: demo.yuvaansoft.shop) ====="
curl -s -o /dev/null -w 'payment-options: %{http_code}\n' \
  -H 'Host: demo.yuvaansoft.shop' http://172.17.0.1:8081/api/health
curl -s -o /dev/null -w 'root page:       %{http_code}\n' \
  -H 'Host: demo.yuvaansoft.shop' http://172.17.0.1:8081/
echo ""
echo "===== RAZORPAY CONFIG SEEN BY DEV API ====="
docker exec smms-dev-api printenv | grep -E '^Razorpay__(Enabled|KeyId)=' || echo "none"
docker exec smms-dev-api printenv | grep -cE '^Razorpay__(KeySecret|WebhookSecret)=.+' | xargs echo "secret vars set:"
echo ""
echo "===== PRODUCTION API RAZORPAY (must be dormant) ====="
docker exec smms-api printenv | grep -E '^Razorpay__' || echo "no Razorpay vars in production - dormant"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

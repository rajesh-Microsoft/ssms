$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# The dev stack's .env predates the allowlist, so the next dev deploy would
# silently turn Razorpay off there. Dev is throwaway, so "*" restores it.
$remote = @'
cd ~/smms-dev/SMMS
if [ ! -f .env ]; then echo "no dev .env found"; exit 0; fi
if grep -q '^RAZORPAY_ALLOWED_SOCIETIES=' .env; then
  echo "already set:"
else
  echo 'RAZORPAY_ALLOWED_SOCIETIES=*' >> .env
  echo "added:"
fi
grep '^RAZORPAY_ALLOWED_SOCIETIES=' .env
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

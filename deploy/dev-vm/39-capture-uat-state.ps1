$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Capture only. Nothing on the VM is modified.
$remote = @'
cd ~/smms/SMMS

git status --porcelain > /tmp/uat-status.txt
git ls-files -m > /tmp/uat-modified.txt
git ls-files -o --exclude-standard > /tmp/uat-untracked.txt
git diff > /tmp/uat-working.patch

echo "===== COUNTS ====="
echo "modified : $(wc -l < /tmp/uat-modified.txt)"
echo "untracked: $(wc -l < /tmp/uat-untracked.txt)"
echo "patch KB : $(du -k /tmp/uat-working.patch | cut -f1)"
echo "base     : $(git rev-parse HEAD)"

echo ""
echo "===== UNTRACKED FILES ====="
cat /tmp/uat-untracked.txt

echo ""
echo "===== CONTENT HASH OF EACH MODIFIED FILE ====="
git hash-object $(cat /tmp/uat-modified.txt) > /tmp/uat-hashes.txt
paste /tmp/uat-hashes.txt /tmp/uat-modified.txt
echo "(end)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

New-Item -ItemType Directory -Force -Path .\uat-capture | Out-Null
scp -i $key "${vm}:/tmp/uat-working.patch"   .\uat-capture\ | Out-Null
scp -i $key "${vm}:/tmp/uat-status.txt"      .\uat-capture\ | Out-Null
scp -i $key "${vm}:/tmp/uat-hashes.txt"      .\uat-capture\ | Out-Null
scp -i $key "${vm}:/tmp/uat-modified.txt"    .\uat-capture\ | Out-Null
scp -i $key "${vm}:/tmp/uat-untracked.txt"   .\uat-capture\ | Out-Null
Write-Output "Captured into .\uat-capture"

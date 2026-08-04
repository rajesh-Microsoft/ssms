$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Read-only on the VM: tar up the running source tree, excluding build output and .git.
$remote = @'
cd ~/smms/SMMS
tar czf /tmp/uat-tree.tgz --exclude=.git --exclude=bin --exclude=obj --exclude=node_modules .
echo "tree tarball KB: $(du -k /tmp/uat-tree.tgz | cut -f1)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

scp -i $key "${vm}:/tmp/uat-tree.tgz" .\uat-capture\ | Out-Null
Write-Output "downloaded uat-tree.tgz"

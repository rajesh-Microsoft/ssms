$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

$remote = @'
cd ~/smms/SMMS
git ls-files -o --exclude-standard > /tmp/u.txt
xargs -a /tmp/u.txt -I{} sh -c 'tr -d "\r" < "{}" | git hash-object --stdin' > /tmp/uat-untracked-hashes.txt
echo "untracked hashed: $(wc -l < /tmp/uat-untracked-hashes.txt)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

scp -i $key "${vm}:/tmp/uat-untracked-hashes.txt" .\uat-capture\ | Out-Null

$hashes = Get-Content .\uat-capture\uat-untracked-hashes.txt
$paths = Get-Content .\uat-capture\uat-untracked.txt

$unique = @()
$match = 0
for ($i = 0; $i -lt $paths.Count; $i++) {
    $blob = git rev-parse "main:$($paths[$i])" 2>$null
    if ($blob -and $blob.Trim() -eq $hashes[$i].Trim()) { $match++ } else { $unique += $paths[$i] }
}

"untracked matching main : $match / $($paths.Count)"
Write-Output ""
Write-Output "--- not identical to main ---"
$unique | ForEach-Object { "  $_" }

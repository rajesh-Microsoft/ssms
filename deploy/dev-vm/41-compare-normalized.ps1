$ErrorActionPreference = 'Stop'
$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

# Hash each modified file with CRs stripped, so line endings stop masking equality.
$remote = @'
cd ~/smms/SMMS
git ls-files -m > /tmp/m.txt
xargs -a /tmp/m.txt -I{} sh -c 'tr -d "\r" < "{}" | git hash-object --stdin' > /tmp/uat-norm-hashes.txt
echo "normalized hashes: $(wc -l < /tmp/uat-norm-hashes.txt)"
'@

$remote = $remote -replace "`r", ""
$remote | ssh -i $key $vm "bash -s"

scp -i $key "${vm}:/tmp/uat-norm-hashes.txt" .\uat-capture\ | Out-Null

$hashes = Get-Content .\uat-capture\uat-norm-hashes.txt
$paths = Get-Content .\uat-capture\uat-modified.txt

foreach ($c in @('main', '4993d30', 'b4c6129', '72f4231')) {
    $match = 0; $differ = 0
    for ($i = 0; $i -lt $paths.Count; $i++) {
        $blob = git rev-parse "${c}:$($paths[$i])" 2>$null
        if ($blob -and $blob.Trim() -eq $hashes[$i].Trim()) { $match++ } else { $differ++ }
    }
    "{0,-10} match={1,-4} differ={2}" -f $c, $match, $differ
}

Write-Output ""
Write-Output "--- genuinely different from main (line endings ignored) ---"
for ($i = 0; $i -lt $paths.Count; $i++) {
    $blob = git rev-parse "main:$($paths[$i])" 2>$null
    if (-not $blob -or $blob.Trim() -ne $hashes[$i].Trim()) { "  $($paths[$i])" }
}

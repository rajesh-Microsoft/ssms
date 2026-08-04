# Compares the UAT working-tree content hashes against local commits to find
# whether the running code already exists in git history.
$hashes = Get-Content .\uat-capture\uat-hashes.txt
$paths = Get-Content .\uat-capture\uat-modified.txt

$candidates = @('main', '4993d30', 'b4c6129', 'ec69a1e', '76bf3e9', 'b01794f', '72f4231')

foreach ($c in $candidates) {
    $match = 0; $differ = 0; $missing = 0
    for ($i = 0; $i -lt $paths.Count; $i++) {
        $ref = "${c}:$($paths[$i])"
        $blob = git rev-parse $ref 2>$null
        if (-not $blob) { $missing++ }
        elseif ($blob.Trim() -eq $hashes[$i].Trim()) { $match++ }
        else { $differ++ }
    }
    "{0,-10} match={1,-4} differ={2,-4} notInCommit={3}" -f $c, $match, $differ, $missing
}

Write-Output ""
Write-Output "--- files that differ from main ---"
for ($i = 0; $i -lt $paths.Count; $i++) {
    $blob = git rev-parse "main:$($paths[$i])" 2>$null
    if ($blob -and $blob.Trim() -ne $hashes[$i].Trim()) { "  $($paths[$i])" }
}

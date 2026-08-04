# For each genuinely-different file, find which commit in main's history holds
# the exact content running on UAT.
$hashes = Get-Content .\uat-capture\uat-norm-hashes.txt
$paths = Get-Content .\uat-capture\uat-modified.txt

$targets = @(
    'UI/api.js', 'UI/app.js', 'UI/index.html', 'UI/partials/mpay.html', 'UI/styles.css',
    'api/SMMS.Api/Program.cs', 'api/SMMS.Api/Services/Control/TenantProvisioningService.cs',
    'api/SMMS.Api/Services/Payments/PaymentService.cs', 'api/SMMS.Api/appsettings.json',
    'docker-compose.yml'
)

foreach ($t in $targets) {
    $i = [Array]::IndexOf($paths, $t)
    $want = $hashes[$i].Trim()
    $found = $null
    foreach ($c in (git rev-list main -- $t)) {
        $blob = git rev-parse "${c}:$t" 2>$null
        if ($blob -and $blob.Trim() -eq $want) { $found = $c; break }
    }
    if ($found) {
        $subj = git log -1 --format='%h %s' $found
        "MATCHED  $t`n         -> $subj"
    }
    else {
        "UNIQUE   $t  (content exists in no commit)"
    }
}

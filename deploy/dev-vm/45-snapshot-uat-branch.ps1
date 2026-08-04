$ErrorActionPreference = 'Stop'

# Records the exact UAT tree as an orphan branch. No parent commit, because the
# tree was hand-assembled from several commits and has no real lineage.
$wt = 'C:\Dev\smms-uat-wt'
$branch = 'uat-snapshot-20260804'

if (Test-Path $wt) { git worktree remove --force $wt }
git worktree add --detach $wt | Out-Null

Push-Location $wt
try {
    git checkout --orphan $branch | Out-Null
    git reset --quiet

    Get-ChildItem -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
    tar -xzf 'C:\Dev\SMMS\uat-capture\uat-tree.tgz' -C $wt

    git add -A
    git -c user.name='SMMS Ops' -c user.email='ops@smms.local' commit --quiet `
        -m "Snapshot of the UAT tree as running on 2026-08-04

Captured from ssms-webserver:~/smms/SMMS. That checkout sat on commit 72f4231
with 59 modified and 63 untracked files, so it matched no single commit. All
content is reproducible from main except Program.cs, which carries the tenant
startup hardening on a pre-Razorpay base."

    "branch : $branch"
    "commit : $(git rev-parse --short HEAD)"
    "files  : $(git ls-files | Measure-Object | Select-Object -ExpandProperty Count)"
}
finally { Pop-Location }

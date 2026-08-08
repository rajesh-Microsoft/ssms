<#
.SYNOPSIS
Copies pre-production's deployment state onto the dev VM so the hosted DevOps portal can
read it.

.DESCRIPTION
The portal runs in a container on the dev VM with no az CLI, no SSH key and no Docker
socket, because it is reachable from the internet. That leaves it unable to read pre-prod,
which lives on a separate VM behind az vm run-command. Rather than hand an internet-facing
service those credentials, the promotion mirrors the three small state files across.

The mirror is written at the moment pre-prod changes, so it is correct by construction.
If it ever does go stale, DEPLOYED_HISTORY carries its own timestamps, so the portal shows
an old deployment date rather than a confidently wrong commit.
#>
[CmdletBinding()]
param(
    [string]$DevVm = '135.235.195.132',
    [string]$DevUser = 'ssmsadmin',
    [string]$KeyPath = "$env:USERPROFILE\ssmsadmin.pem",
    [string]$MirrorPath = '~/devportal/state/pprod'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$reader = New-TemporaryFile
@'
#!/bin/bash
cd /opt/smms/app
echo "__MIRROR_COMMIT__"
cat DEPLOYED_COMMIT 2>/dev/null | tr -d '\r\n'; echo ""
echo "__MIRROR_VERIFIED__"
cat DEPLOYED_VERIFIED 2>/dev/null | tr -d '\r\n'; echo ""
echo "__MIRROR_HISTORY__"
tail -n 15 DEPLOYED_HISTORY 2>/dev/null
echo "__MIRROR_END__"
'@ | Set-Content -Path $reader.FullName -Encoding ascii

$out = & (Join-Path $here 'run-on-pprod.ps1') -ScriptPath $reader.FullName | Out-String
Remove-Item $reader.FullName -ErrorAction SilentlyContinue

if ($out -notmatch '__MIRROR_END__') { throw "could not read pre-prod state; mirror not updated" }

function Section([string]$Name) {
    # ${Name} braces are required: "$Name__" would parse the trailing underscores as
    # part of the variable name and silently expand to nothing.
    $m = [regex]::Match($out, "__MIRROR_${Name}__\r?\n(.*?)(?=__MIRROR_)", 'Singleline')
    if (-not $m.Success) { return '' }
    $m.Groups[1].Value.Trim("`r", "`n", ' ')
}
$commit = Section 'COMMIT'
$verified = Section 'VERIFIED'
$history = Section 'HISTORY'

if ($commit -notmatch '^[0-9a-f]{40}$') { throw "pre-prod returned an implausible commit: '$commit'" }
Write-Host "pre-prod is at $($commit.Substring(0,7))" -ForegroundColor Cyan

# Written with a here-doc so the payload never goes through the shell's word splitting.
$remote = @"
set -e
mkdir -p $MirrorPath
cat > $MirrorPath/DEPLOYED_COMMIT <<'EOF_C'
$commit
EOF_C
cat > $MirrorPath/DEPLOYED_VERIFIED <<'EOF_V'
$verified
EOF_V
cat > $MirrorPath/DEPLOYED_HISTORY <<'EOF_H'
$history
EOF_H
echo "mirrored to $MirrorPath"
"@ -replace "`r", ""

$remote | ssh -i $KeyPath -o BatchMode=yes -o StrictHostKeyChecking=accept-new "$DevUser@$DevVm" 'bash -s'
if ($LASTEXITCODE -ne 0) { throw "writing the mirror to the dev VM failed (is the JIT window open?)" }
Write-Host 'Portal mirror updated.' -ForegroundColor Green

$ErrorActionPreference = 'Stop'
$sub = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d'
$url = (Get-Content -Raw (Join-Path $PSScriptRoot 'stage-url.txt')).Trim()

# Built inline so the SAS token is never written to a file on disk or echoed to the console.
$bash = @"
#!/bin/bash
set -euo pipefail
mkdir -p /opt/smms
curl -fsSL '$url' -o /tmp/smms.tar.gz
rm -rf /opt/smms/app
mkdir -p /opt/smms/app
tar -xzf /tmp/smms.tar.gz -C /opt/smms/app
rm -f /tmp/smms.tar.gz
chown -R ssmsadmin:ssmsadmin /opt/smms
echo '--- extracted ---'
ls /opt/smms/app
echo '--- key files ---'
ls /opt/smms/app/docker-compose.yml /opt/smms/app/nginx/nginx.conf /opt/smms/app/api/SMMS.Api/Dockerfile
"@ -replace "`r", ""

$tmp = New-TemporaryFile
[System.IO.File]::WriteAllText($tmp.FullName, $bash)
$raw = az vm run-command invoke --subscription $sub -g smms-pprod-rg -n smms-pprod-vm `
    --command-id RunShellScript --scripts "@$($tmp.FullName)" -o json 2>&1
Remove-Item $tmp.FullName -ErrorAction SilentlyContinue

try { ($raw | ConvertFrom-Json).value | ForEach-Object { $_.message } } catch { Write-Output $raw }

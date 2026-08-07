$ErrorActionPreference = 'Stop'
$sub  = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d'
$here = $PSScriptRoot

$tmpl   = (Get-Content -Raw (Join-Path $here '5-configure.sh'))
$nginx  = (Get-Content -Raw (Join-Path $here 'nginx.pprod.conf'))
$compose= (Get-Content -Raw (Join-Path $here 'docker-compose.yml'))

$script = $tmpl.Replace('__NGINX_CONF__', $nginx).Replace('__COMPOSE__', $compose) -replace "`r", ""

$tmp = New-TemporaryFile
[System.IO.File]::WriteAllText($tmp.FullName, $script)
$raw = az vm run-command invoke --subscription $sub -g smms-pprod-rg -n smms-pprod-vm `
    --command-id RunShellScript --scripts "@$($tmp.FullName)" -o json 2>&1
Remove-Item $tmp.FullName -ErrorAction SilentlyContinue

try { ($raw | ConvertFrom-Json).value | ForEach-Object { $_.message } } catch { Write-Output $raw }

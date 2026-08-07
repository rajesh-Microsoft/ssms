# Runs a local shell script on the pre-prod VM through the Azure agent.
# SSH from this workstation is blocked by local endpoint security, so run-command is the transport.
param(
    [Parameter(Mandatory)][string]$ScriptPath,
    [string]$Sub = '372ae97b-77d2-4a3a-91c4-9fec0b893a7d',
    [string]$Rg = 'smms-pprod-rg',
    [string]$Vm = 'smms-pprod-vm'
)
$ErrorActionPreference = 'Stop'

# CRLF would break bash on the VM.
$tmp = New-TemporaryFile
$body = (Get-Content -Raw $ScriptPath) -replace "`r", ""
[System.IO.File]::WriteAllText($tmp.FullName, $body)

$raw = az vm run-command invoke --subscription $Sub -g $Rg -n $Vm `
    --command-id RunShellScript --scripts "@$($tmp.FullName)" -o json 2>&1
Remove-Item $tmp.FullName -ErrorAction SilentlyContinue

try { $json = $raw | ConvertFrom-Json } catch { Write-Output $raw; exit 1 }
$json.value | ForEach-Object { Write-Output $_.message }

<#
.SYNOPSIS
Changes a society admin's password without the password ever being typed on a command
line, echoed, or written to a file.

.DESCRIPTION
The seeded default (admin/admin123 from DbSeeder) is live on environments holding real
resident data. This signs in as the account, changes its password through the app's own
/api/me/change-password endpoint, and verifies the new one works.

Passwords are read with Read-Host -AsSecureString, so they stay out of the terminal
history, the screen, and any transcript.

.EXAMPLE
  ./tools/change-admin-password.ps1 -SiteHost demo.yuvaansoft.shop -Username admin
  ./tools/change-admin-password.ps1 -SiteHost pprod-aadya.ssms.yuvaansoft.shop -Username Admin
#>
[CmdletBinding()]
param(
    # Not -Host: PowerShell reserves $Host as a read-only automatic variable.
    [Parameter(Mandatory)][string]$SiteHost,
    [string]$Username = 'admin'
)

$ErrorActionPreference = 'Stop'
$base = "https://$SiteHost"

function Read-Plain([string]$Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    [System.Net.NetworkCredential]::new('', $secure).Password
}

Write-Host "Changing the password for '$Username' on $SiteHost" -ForegroundColor Cyan
$current = Read-Plain 'Current password'
$new = Read-Plain 'New password'
$confirm = Read-Plain 'New password again'

if ($new -ne $confirm) { throw 'The two new passwords do not match.' }
if ($new.Length -lt 12) { throw 'Use at least 12 characters: this account holds real resident data.' }
if ($new -eq $current) { throw 'The new password is the same as the current one.' }

$login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType 'application/json' `
    -Body (@{ username = $Username; password = $current } | ConvertTo-Json)

if (-not $login.token) { throw 'Sign-in failed: the current password is wrong.' }
Write-Host 'Signed in.' -ForegroundColor Green

Invoke-RestMethod -Method Post -Uri "$base/api/me/change-password" -ContentType 'application/json' `
    -Headers @{ Authorization = "Bearer $($login.token)" } `
    -Body (@{ currentPassword = $current; newPassword = $new } | ConvertTo-Json) | Out-Null
Write-Host 'Password changed.' -ForegroundColor Green

# Prove the new one works and the old one does not, rather than assuming.
$verify = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType 'application/json' `
    -Body (@{ username = $Username; password = $new } | ConvertTo-Json)
if (-not $verify.token) { throw 'The new password did not work. Investigate before closing this window.' }
Write-Host 'Verified: the new password signs in.' -ForegroundColor Green

try {
    Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType 'application/json' `
        -Body (@{ username = $Username; password = $current } | ConvertTo-Json) | Out-Null
    Write-Warning 'The OLD password still signs in. Something is wrong: investigate.'
}
catch {
    Write-Host 'Verified: the old password no longer works.' -ForegroundColor Green
}

$current = $new = $confirm = $null
[System.GC]::Collect()

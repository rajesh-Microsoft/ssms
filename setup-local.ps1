[CmdletBinding()]
param(
    [string]$BranchName = "dev/$($env:USERNAME.ToLowerInvariant())",
    [switch]$SkipInstall,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Refresh-Path {
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machinePath;$userPath"
}

function Ensure-WingetPackage([string]$Command, [string]$PackageId, [string]$DisplayName) {
    if (Get-Command $Command -ErrorAction SilentlyContinue) {
        Write-Host "$DisplayName is already installed."
        return
    }

    if ($SkipInstall) {
        throw "$DisplayName is required but was not found. Re-run without -SkipInstall."
    }

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget is required to install $DisplayName. Install App Installer from the Microsoft Store, then re-run this script."
    }

    Write-Step "Installing $DisplayName"
    & winget install --id $PackageId --exact --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget could not install $DisplayName (exit code $LASTEXITCODE)."
    }
    Refresh-Path
}

function Get-VsCodeCommand {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\bin\code.cmd'),
        (Join-Path $env:ProgramFiles 'Microsoft VS Code\bin\code.cmd')
    )
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Ensure-VsCode {
    $command = Get-VsCodeCommand
    if ($command) { return $command }

    if ($SkipInstall) {
        throw 'Visual Studio Code is required but was not found. Re-run without -SkipInstall.'
    }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'winget is required to install Visual Studio Code. Install App Installer from the Microsoft Store, then re-run this script.'
    }

    Write-Step 'Installing Visual Studio Code'
    & winget install --id Microsoft.VisualStudioCode --exact --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget could not install Visual Studio Code (exit code $LASTEXITCODE)."
    }
    Refresh-Path

    $command = Get-VsCodeCommand
    if (-not $command) {
        throw 'Visual Studio Code was installed but its command-line tool could not be located. Reopen PowerShell and run this script again.'
    }
    return $command
}

function Wait-ForDocker([int]$TimeoutSeconds = 180) {
    & docker info *> $null
    if ($LASTEXITCODE -eq 0) { return }

    $dockerDesktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (Test-Path $dockerDesktop) {
        Write-Step 'Starting Docker Desktop'
        Start-Process $dockerDesktop
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 3
        & docker info *> $null
        if ($LASTEXITCODE -eq 0) { return }
    } while ((Get-Date) -lt $deadline)

    throw 'Docker Desktop did not become ready. Open Docker Desktop, finish its first-run setup, then run this script again.'
}

function Assert-GitSuccess([string]$Action) {
    if ($LASTEXITCODE -ne 0) {
        throw "Git failed while $Action (exit code $LASTEXITCODE)."
    }
}

$repoRoot = $PSScriptRoot
Set-Location $repoRoot

if (-not (Test-Path (Join-Path $repoRoot '.git'))) {
    throw 'Run this script from a Git clone of SMMS.'
}

Ensure-WingetPackage -Command git -PackageId Git.Git -DisplayName Git
$vsCodeCommand = Ensure-VsCode
Ensure-WingetPackage -Command docker -PackageId Docker.DockerDesktop -DisplayName 'Docker Desktop'
Ensure-WingetPackage -Command age -PackageId FiloSottile.age -DisplayName age

Write-Step "Preparing branch $BranchName"
$invalidBranch = & git check-ref-format --branch $BranchName 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Invalid branch name '$BranchName': $invalidBranch"
}

$currentBranch = (& git branch --show-current).Trim()
Assert-GitSuccess 'reading the current branch'

if ($currentBranch -ne $BranchName) {
    $changes = & git status --porcelain
    Assert-GitSuccess 'checking the working tree'
    if ($changes) {
        throw "The working tree has changes. Commit or stash them before switching to '$BranchName'."
    }

    & git fetch origin
    Assert-GitSuccess 'fetching origin'

    & git show-ref --verify --quiet "refs/heads/$BranchName"
    if ($LASTEXITCODE -eq 0) {
        & git switch $BranchName
    }
    else {
        & git show-ref --verify --quiet "refs/remotes/origin/$BranchName"
        if ($LASTEXITCODE -eq 0) {
            & git switch --track "origin/$BranchName"
        }
        else {
            & git switch --create $BranchName origin/main
        }
    }
    Assert-GitSuccess "switching to $BranchName"
}
else {
    Write-Host "Already on $BranchName."
}

if (-not (Test-Path '.\docker-compose.override.yml')) {
    Copy-Item '.\docker-compose.override.example.yml' '.\docker-compose.override.yml'
    Write-Host 'Created docker-compose.override.yml.'
}
else {
    Write-Host 'Keeping existing docker-compose.override.yml.'
}

if (-not (Test-Path '.\.env')) {
    Copy-Item '.\.env.example' '.\.env'
    Write-Host 'Created .env with payment integrations disabled.'
}
else {
    Write-Host 'Keeping existing .env.'
}

Write-Step 'Installing recommended VS Code extensions'
$extensions = @(
    'ms-azuretools.vscode-docker',
    'ms-dotnettools.csdevkit',
    'ms-vscode.powershell'
)
$installedExtensions = @(& $vsCodeCommand --list-extensions)
foreach ($extension in $extensions) {
    if ($installedExtensions -contains $extension) {
        Write-Host "$extension is already installed."
        continue
    }
    & $vsCodeCommand --install-extension $extension | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not install VS Code extension $extension."
    }
}

Wait-ForDocker

Write-Step 'Starting the local SMMS containers'
$composeArgs = @('compose', 'up', '-d')
if (-not $SkipBuild) { $composeArgs += '--build' }
& docker @composeArgs
if ($LASTEXITCODE -ne 0) {
    throw "docker compose failed (exit code $LASTEXITCODE)."
}

Write-Step 'Waiting for the API and database'
$healthUrl = 'http://admin.localtest.me:8080/api/health'
$deadline = (Get-Date).AddMinutes(3)
$healthy = $false
do {
    try {
        $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 5
        if ($health.status -eq 'Healthy') {
            $healthy = $true
            break
        }
    }
    catch { }
    if (-not $healthy) { Start-Sleep -Seconds 3 }
} while ((Get-Date) -lt $deadline)

if (-not $healthy) {
    & docker compose ps | Out-Host
    & docker compose logs --tail 80 smms-api | Out-Host
    throw "SMMS did not become healthy at $healthUrl. Container status and API logs are shown above."
}

Write-Step 'Local setup is ready'
Write-Host "Branch:       $BranchName"
Write-Host 'Admin portal: http://admin.localtest.me:8080'
Write-Host 'Tenant site:  http://aadya.localtest.me:8080'
Write-Host 'API health:   http://admin.localtest.me:8080/api/health'
Write-Host 'Stop:         docker compose down'

& $vsCodeCommand $repoRoot

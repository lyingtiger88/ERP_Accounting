$ErrorActionPreference = "Stop"

Write-Host "== ERP Accounting: .NET 10 SDK installer ==" -ForegroundColor Cyan

$localRoot = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
$localDotnet = Join-Path $localRoot "dotnet.exe"

function Get-DotNet10Sdk {
    param([string]$DotNetExe)
    if (-not (Test-Path $DotNetExe)) { return $null }
    try { return (& $DotNetExe --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }) }
    catch { return $null }
}

$currentDotNet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($currentDotNet) {
    $existing = Get-DotNet10Sdk -DotNetExe $currentDotNet.Source
    if ($existing) {
        Write-Host ".NET 10 SDK is already installed:" -ForegroundColor Green
        $existing | ForEach-Object { Write-Host "  $_" }
        exit 0
    }
}

$localExisting = Get-DotNet10Sdk -DotNetExe $localDotnet
if ($localExisting) {
    Write-Host ".NET 10 SDK is already installed for this user:" -ForegroundColor Green
    $localExisting | ForEach-Object { Write-Host "  $_" }
    exit 0
}

$hasWinget = Get-Command winget -ErrorAction SilentlyContinue
if ($hasWinget) {
    Write-Host "Trying Microsoft .NET SDK 10 installation via WinGet..." -ForegroundColor Yellow
    $arguments = @("install","--id","Microsoft.DotNet.SDK.10","--source","winget","--accept-source-agreements","--accept-package-agreements")
    try {
        $process = Start-Process -FilePath "winget.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        if ($process.ExitCode -eq 0) {
            $systemDotNet = Get-Command dotnet -ErrorAction SilentlyContinue
            if ($systemDotNet) {
                $installed = Get-DotNet10Sdk -DotNetExe $systemDotNet.Source
                if ($installed) {
                    Write-Host ".NET 10 SDK setup finished." -ForegroundColor Green
                    $installed | ForEach-Object { Write-Host "  $_" }
                    exit 0
                }
            }
        }
        Write-Host "WinGet did not complete setup. Falling back to Microsoft dotnet-install.ps1." -ForegroundColor Yellow
    }
    catch {
        Write-Host "WinGet could not be used. Falling back to Microsoft dotnet-install.ps1." -ForegroundColor Yellow
    }
}
else {
    Write-Host "WinGet was not found. Using Microsoft dotnet-install.ps1 instead." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Installing .NET 10 SDK for the current user..." -ForegroundColor Cyan
Write-Host "Target: $localRoot"
New-Item -ItemType Directory -Path $localRoot -Force | Out-Null

$installerPath = Join-Path $env:TEMP "erp-dotnet-install.ps1"
Invoke-WebRequest -UseBasicParsing -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installerPath

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath -Channel "10.0" -InstallDir $localRoot -NoPath
if ($LASTEXITCODE -ne 0) { throw "Microsoft dotnet-install.ps1 failed with exit code $LASTEXITCODE." }

$installed10 = Get-DotNet10Sdk -DotNetExe $localDotnet
if (-not $installed10) { throw ".NET 10 installation completed, but the SDK could not be detected at $localRoot." }

$env:DOTNET_ROOT = $localRoot
if (($env:Path -split ";") -notcontains $localRoot) { $env:Path = "$localRoot;$env:Path" }
[Environment]::SetEnvironmentVariable("DOTNET_ROOT", $localRoot, "User")

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$userEntries = @()
if (-not [string]::IsNullOrWhiteSpace($userPath)) { $userEntries = $userPath -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } }
if ($userEntries -notcontains $localRoot) {
    $newUserPath = (($localRoot) + $userEntries) -join ";"
    [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
}

Write-Host ""
Write-Host ".NET 10 SDK installed successfully:" -ForegroundColor Green
$installed10 | ForEach-Object { Write-Host "  $_" }
Write-Host "Active local dotnet:" -ForegroundColor Cyan
& $localDotnet --version
Write-Host ""
Write-Host "Close this window and run Start_ERP_Dev.bat again." -ForegroundColor Green
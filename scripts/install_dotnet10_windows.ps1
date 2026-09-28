$ErrorActionPreference = "Stop"

Write-Host "== ERP Accounting: .NET 10 SDK installer ==" -ForegroundColor Cyan

$hasWinget = Get-Command winget -ErrorAction SilentlyContinue
if (-not $hasWinget) {
    throw "WinGet was not found. Install App Installer from Microsoft Store or install .NET 10 SDK manually from https://dotnet.microsoft.com/download/dotnet/10.0"
}

$installed10 = dotnet --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }
if ($installed10) {
    Write-Host ".NET 10 SDK is already installed:" -ForegroundColor Green
    $installed10 | ForEach-Object { Write-Host "  $_" }
    exit 0
}

Write-Host "Installing Microsoft .NET SDK 10 via WinGet..." -ForegroundColor Yellow

$arguments = @(
    "install",
    "--id", "Microsoft.DotNet.SDK.10",
    "--source", "winget",
    "--accept-source-agreements",
    "--accept-package-agreements"
)

$process = Start-Process -FilePath "winget.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru

if ($process.ExitCode -ne 0) {
    throw "WinGet failed to install .NET 10 SDK. Exit code: $($process.ExitCode)"
}

Write-Host ""
Write-Host "Installed SDKs:" -ForegroundColor Cyan
dotnet --list-sdks

$installed10 = dotnet --list-sdks | Where-Object { $_ -match '^10\.' }
if (-not $installed10) {
    Write-Host ""
    Write-Host "Installation completed but this terminal has not detected .NET 10 yet." -ForegroundColor Yellow
    Write-Host "Close this window, open Start_ERP_Dev.bat again, and retry." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host ".NET 10 SDK setup finished." -ForegroundColor Green

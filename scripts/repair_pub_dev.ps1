$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$client = Join-Path $root "apps\client_flutter"
$flutter = "C:\src\flutter\bin\flutter.bat"
$dart = "C:\src\flutter\bin\dart.bat"

if (-not (Test-Path $flutter) -or -not (Test-Path $dart)) {
    throw "Flutter/Dart was not found under C:\src\flutter\bin."
}

Write-Host "== ERP Accounting: Repair pub.dev ==" -ForegroundColor Cyan
Write-Host ("Dart: " + (& $dart --version 2>&1))
Write-Host ""

if ([string]::IsNullOrWhiteSpace($env:PUB_HOSTED_URL)) {
    Write-Host "PUB_HOSTED_URL: not set"
} else {
    Write-Host ("PUB_HOSTED_URL: " + $env:PUB_HOSTED_URL) -ForegroundColor Yellow
}

foreach ($name in @("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY")) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        Write-Host ($name + ": not set")
    } else {
        Write-Host ($name + ": SET") -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Configured pub tokens:"
& $dart pub token list
Write-Host ""

try {
    & $dart pub token remove https://pub.dev 2>$null | Out-Host
} catch {
}

$credentialFile = Join-Path $env:APPDATA "dart\pub-credentials.json"
if (Test-Path $credentialFile) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backup = $credentialFile + ".bak-" + $stamp
    Copy-Item $credentialFile $backup -Force
    Remove-Item $credentialFile -Force
    Write-Host ("Backed up legacy pub credentials to: " + $backup) -ForegroundColor Yellow
}

$env:PUB_HOSTED_URL = "https://pub.dev"

Write-Host ""
Write-Host "Testing direct access to pub.dev..." -ForegroundColor Cyan
try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri "https://pub.dev/api/packages/sqflite_common_ffi" -TimeoutSec 30
    Write-Host ("pub.dev HTTP status: " + [int]$response.StatusCode) -ForegroundColor Green
} catch {
    Write-Host "Direct HTTPS test failed." -ForegroundColor Red
    if ($_.Exception.Response) {
        try {
            Write-Host ("HTTP status: " + [int]$_.Exception.Response.StatusCode)
        } catch {}
    }
    Write-Host "If this is HTTP 403, the network/proxy/VPN path is being rejected before Flutter package resolution." -ForegroundColor Yellow
    throw
}

Write-Host ""
Write-Host "Running flutter pub get..." -ForegroundColor Cyan
Set-Location $client
& $flutter pub get

if ($LASTEXITCODE -ne 0) {
    throw "flutter pub get still failed after pub.dev repair."
}

Write-Host ""
Write-Host "pub.dev repair succeeded." -ForegroundColor Green
Write-Host "You can now run Start_Client_Dev.bat"
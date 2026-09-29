$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$client = Join-Path $root "apps\client_flutter"

$flutterExe = $null

$flutterCommand = Get-Command flutter -ErrorAction SilentlyContinue
if ($flutterCommand) {
    $flutterExe = $flutterCommand.Source
}

if (-not $flutterExe) {
    $recommended = "C:\src\flutter\bin\flutter.bat"
    if (Test-Path $recommended) {
        $flutterExe = $recommended
        $env:Path = "C:\src\flutter\bin;$env:Path"
    }
}

if (-not $flutterExe) {
    Write-Host "Flutter SDK was not found." -ForegroundColor Yellow
    Write-Host "Run Install_Flutter_Windows.bat first." -ForegroundColor Cyan
    throw "Flutter SDK is required."
}

$flutterRoot = Split-Path -Parent (Split-Path -Parent $flutterExe)
$dartExe = Join-Path $flutterRoot "bin\dart.bat"

Write-Host "ERP Accounting Windows Client" -ForegroundColor Cyan
Write-Host ("Using Flutter: " + $flutterExe)
Write-Host ("Client path: " + $client)
Write-Host ""

if (Test-Path $dartExe) {
    $tokenList = & $dartExe pub token list 2>&1 | Out-String

    if ($tokenList -match "https://pub\.dev/?") {
        Write-Host "A saved pub.dev authentication token was detected." -ForegroundColor Yellow
        Write-Host "Public pub.dev package downloads do not require a token." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Run this command once, then restart this launcher:" -ForegroundColor Cyan
        Write-Host ('  & "' + $dartExe + '" pub token remove https://pub.dev') -ForegroundColor Cyan
        Write-Host ""
        throw "Invalid/unnecessary pub.dev credential may block package restore."
    }
}

if (-not [string]::IsNullOrWhiteSpace($env:PUB_HOSTED_URL)) {
    Write-Host ("PUB_HOSTED_URL = " + $env:PUB_HOSTED_URL) -ForegroundColor DarkGray
}

Set-Location $client
& $flutterExe pub get

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Package restore failed." -ForegroundColor Red
    Write-Host "If the message contains 'authorization failed' for https://pub.dev, run:" -ForegroundColor Yellow
    Write-Host ('  & "' + $dartExe + '" pub token list') -ForegroundColor Cyan
    Write-Host ('  & "' + $dartExe + '" pub token remove https://pub.dev') -ForegroundColor Cyan
    throw "flutter pub get failed."
}

& $flutterExe run -d windows

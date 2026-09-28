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

Write-Host "ERP Accounting Windows Client" -ForegroundColor Cyan
Write-Host ("Using Flutter: " + $flutterExe)
Write-Host ("Client path: " + $client)
Write-Host ""

Set-Location $client
& $flutterExe pub get
if ($LASTEXITCODE -ne 0) { throw "flutter pub get failed." }

& $flutterExe run -d windows
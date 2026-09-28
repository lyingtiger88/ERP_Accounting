param(
    [switch]$SkipFlutterCreate
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "== ERP Accounting developer bootstrap ==" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK 10 is required."
}

if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
    throw "Flutter SDK is required."
}

$dotnetVersion = dotnet --version
Write-Host ".NET: $dotnetVersion"

$flutterVersion = (flutter --version | Select-Object -First 1)
Write-Host "Flutter: $flutterVersion"

$client = Join-Path $root "apps/client_flutter"
$server = Join-Path $root "apps/server_api"

if (-not $SkipFlutterCreate) {
    Push-Location $client
    try {
        if (-not (Test-Path "windows") -or -not (Test-Path "android")) {
            Write-Host "Generating Flutter Windows/Android platform runners..."
            flutter create --platforms=windows,android --project-name erp_accounting .
        }
        flutter pub get
    }
    finally {
        Pop-Location
    }
}

Push-Location $server
try {
    Write-Host "Restoring .NET API..."
    dotnet restore
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Bootstrap complete." -ForegroundColor Green
Write-Host "API:     cd apps/server_api; dotnet run"
Write-Host "Windows: cd apps/client_flutter; flutter run -d windows"
Write-Host "Android: cd apps/client_flutter; flutter run -d android"

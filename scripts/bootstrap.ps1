param(
    [switch]$SkipFlutterCreate
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "== ERP Accounting developer bootstrap ==" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK 10 is required."
}

# If Flutter was just installed in the project-recommended location but the
# current terminal has an old PATH snapshot, make it available immediately.
if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
    $recommendedFlutterBin = "C:\src\flutter\bin"
    if (Test-Path (Join-Path $recommendedFlutterBin "flutter.bat")) {
        $env:Path = "$recommendedFlutterBin;$env:Path"
    }
}

if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "Flutter SDK was not found." -ForegroundColor Yellow
    Write-Host "Run the one-click installer from the repository root:" -ForegroundColor Yellow
    Write-Host "    .\Install_Flutter_Windows.bat" -ForegroundColor Cyan
    Write-Host ""
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

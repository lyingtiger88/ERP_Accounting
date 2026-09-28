param(
    [switch]$SkipFlutterCreate
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "== ERP Accounting developer bootstrap ==" -ForegroundColor Cyan

function Get-DotNetSdks {
    param([string]$DotNetExe)
    if (-not (Test-Path $DotNetExe)) { return @() }
    try { return @(& $DotNetExe --list-sdks 2>$null) }
    catch { return @() }
}

$localDotnetRoot = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
$localDotnetExe = Join-Path $localDotnetRoot "dotnet.exe"
$dotnetExe = $null
$dotnetSdks = @()

if (Test-Path $localDotnetExe) {
    $localSdks = Get-DotNetSdks -DotNetExe $localDotnetExe
    if ($localSdks | Where-Object { $_ -match '^10\.' }) {
        $dotnetExe = $localDotnetExe
        $dotnetSdks = $localSdks
        $env:DOTNET_ROOT = $localDotnetRoot
        $env:Path = "$localDotnetRoot;$env:Path"
    }
}

if (-not $dotnetExe) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) {
        $candidateSdks = Get-DotNetSdks -DotNetExe $dotnetCommand.Source
        if ($candidateSdks | Where-Object { $_ -match '^10\.' }) {
            $dotnetExe = $dotnetCommand.Source
            $dotnetSdks = $candidateSdks
        } else {
            $dotnetSdks = $candidateSdks
        }
    }
}

if (-not $dotnetExe) {
    Write-Host ""
    Write-Host ".NET 10 SDK was not found." -ForegroundColor Yellow
    if ($dotnetSdks.Count -gt 0) {
        Write-Host "Detected SDKs:" -ForegroundColor Yellow
        $dotnetSdks | ForEach-Object { Write-Host "  $_" }
    }
    Write-Host ""
    Write-Host "Run: .\Install_DotNet10_Windows.bat" -ForegroundColor Cyan
    throw ".NET SDK 10 is required."
}

$dotnet10 = $dotnetSdks | Where-Object { $_ -match '^10\.' }

if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
    $recommendedFlutterBin = "C:\src\flutter\bin"
    if (Test-Path (Join-Path $recommendedFlutterBin "flutter.bat")) { $env:Path = "$recommendedFlutterBin;$env:Path" }
}

if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "Flutter SDK was not found." -ForegroundColor Yellow
    Write-Host "Run: .\Install_Flutter_Windows.bat" -ForegroundColor Cyan
    throw "Flutter SDK is required."
}

$dotnetVersion = & $dotnetExe --version
Write-Host ".NET active SDK: $dotnetVersion"
Write-Host ".NET 10 detected: $($dotnet10 | Select-Object -First 1)"

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
    } finally { Pop-Location }
}

Push-Location $server
try {
    Write-Host "Restoring .NET API..."
    & $dotnetExe restore
} finally { Pop-Location }

Write-Host ""
Write-Host "Bootstrap complete." -ForegroundColor Green
Write-Host "API:     cd apps/server_api; dotnet run"
Write-Host "Windows: cd apps/client_flutter; flutter run -d windows"
Write-Host "Android: cd apps/client_flutter; flutter run -d android"
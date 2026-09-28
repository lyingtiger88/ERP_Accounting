param(
    [string]$InstallRoot = "C:\src"
)

$ErrorActionPreference = "Stop"

Write-Host "== ERP Accounting: Flutter installer ==" -ForegroundColor Cyan

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git for Windows is required before installing Flutter."
}

$flutterDir = Join-Path $InstallRoot "flutter"
$flutterBin = Join-Path $flutterDir "bin"

if (-not (Test-Path $InstallRoot)) {
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
}

if (-not (Test-Path (Join-Path $flutterDir ".git"))) {
    Write-Host "Downloading Flutter stable from the official Flutter repository..."
    git clone --depth 1 --branch stable https://github.com/flutter/flutter.git $flutterDir
}
else {
    Write-Host "Flutter already exists at $flutterDir"
    Write-Host "Updating stable branch..."
    git -C $flutterDir fetch origin stable --depth 1
    git -C $flutterDir checkout stable
    git -C $flutterDir pull --ff-only origin stable
}

# Add Flutter to the current process immediately.
if (($env:Path -split ";") -notcontains $flutterBin) {
    $env:Path = "$flutterBin;$env:Path"
}

# Persist Flutter in the current user's PATH.
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$userEntries = @()
if (-not [string]::IsNullOrWhiteSpace($userPath)) {
    $userEntries = $userPath -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

if ($userEntries -notcontains $flutterBin) {
    $newUserPath = (($flutterBin) + $userEntries) -join ";"
    [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
    Write-Host "Added Flutter to the user PATH." -ForegroundColor Green
}
else {
    Write-Host "Flutter is already present in the user PATH."
}

Write-Host ""
Write-Host "Flutter version:" -ForegroundColor Cyan
flutter --version

Write-Host ""
Write-Host "Running flutter doctor..." -ForegroundColor Cyan
flutter doctor

Write-Host ""
Write-Host "Flutter SDK setup finished." -ForegroundColor Green
Write-Host "Close and reopen terminals/IDEs if they were already open before this installation."

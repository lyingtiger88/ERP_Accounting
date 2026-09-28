$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$server = Join-Path $root "apps\server_api"

$localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$dotnetExe = $null

function Has-DotNet10 {
    param([string]$Exe)
    if (-not (Test-Path $Exe)) { return $false }
    try {
        return [bool]((& $Exe --list-sdks 2>$null) | Where-Object { $_ -match '^10\.' })
    } catch {
        return $false
    }
}

if (Has-DotNet10 $localDotnet) {
    $dotnetExe = $localDotnet
    $localRoot = Split-Path -Parent $localDotnet
    $env:DOTNET_ROOT = $localRoot
    $env:Path = "$localRoot;$env:Path"
} else {
    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($systemDotnet -and (Has-DotNet10 $systemDotnet.Source)) {
        $dotnetExe = $systemDotnet.Source
    }
}

if (-not $dotnetExe) {
    Write-Host ".NET 10 SDK was not found." -ForegroundColor Yellow
    Write-Host "Run Install_DotNet10_Windows.bat first." -ForegroundColor Cyan
    throw ".NET 10 SDK is required."
}

Write-Host "ERP Accounting API" -ForegroundColor Cyan
Write-Host ("Using dotnet: " + $dotnetExe)
Write-Host ("SDK: " + (& $dotnetExe --version))
Write-Host ("Server path: " + $server)
Write-Host ""

Set-Location $server
& $dotnetExe run
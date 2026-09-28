$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== ERP Accounting: PostgreSQL development database ==" -ForegroundColor Cyan

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "Docker was not found." -ForegroundColor Yellow
    Write-Host "Install Docker Desktop, or install PostgreSQL 18 locally." -ForegroundColor Yellow
    Write-Host "The development connection expects:" -ForegroundColor Yellow
    Write-Host "  Host: 127.0.0.1"
    Write-Host "  Port: 5432"
    Write-Host "  Database: erp_accounting"
    Write-Host "  User: erp"
    Write-Host "  Password: erp_dev_password"
    throw "PostgreSQL runtime is required."
}

Write-Host "Starting PostgreSQL container..." -ForegroundColor Cyan
docker compose up -d postgres

Write-Host "Waiting for database health..." -ForegroundColor Cyan
for ($i = 0; $i -lt 30; $i++) {
    $status = docker inspect --format="{{.State.Health.Status}}" erp-accounting-postgres 2>$null
    if ($status -eq "healthy") {
        Write-Host "PostgreSQL is ready." -ForegroundColor Green
        exit 0
    }
    Start-Sleep -Seconds 2
}

throw "PostgreSQL container did not become healthy in time. Run: docker compose logs postgres"
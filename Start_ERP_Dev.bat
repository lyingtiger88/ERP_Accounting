@echo off
setlocal

set "ROOT=%~dp0"

start "ERP Accounting Dev" powershell.exe -NoExit -ExecutionPolicy Bypass -Command ^
  "Set-Location -LiteralPath '%ROOT%'; Write-Host 'ERP Accounting Development Terminal' -ForegroundColor Cyan; Write-Host ('Project: ' + (Get-Location)); & '.\scripts\bootstrap.ps1'"

endlocal

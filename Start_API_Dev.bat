@echo off
setlocal
set "ROOT=%~dp0"
start "ERP Accounting API" powershell.exe -NoExit -ExecutionPolicy Bypass -File "%ROOT%scripts\run_api_dev.ps1"
endlocal
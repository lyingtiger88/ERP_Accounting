@echo off
setlocal
set "ROOT=%~dp0"
start "ERP Accounting Client" powershell.exe -NoExit -ExecutionPolicy Bypass -File "%ROOT%scripts\run_client_windows_dev.ps1"
endlocal
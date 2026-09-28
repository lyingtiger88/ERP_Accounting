@echo off
setlocal
set "ROOT=%~dp0"
powershell.exe -NoExit -ExecutionPolicy Bypass -File "%ROOT%scripts\start_postgres_dev.ps1"
endlocal
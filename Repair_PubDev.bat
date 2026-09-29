@echo off
setlocal
set "ROOT=%~dp0"
start "Repair pub.dev" powershell.exe -NoExit -ExecutionPolicy Bypass -File "%ROOT%scripts\repair_pub_dev.ps1"
endlocal
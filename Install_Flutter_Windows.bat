@echo off
setlocal
set "ROOT=%~dp0"

powershell.exe -NoExit -ExecutionPolicy Bypass -File "%ROOT%scripts\install_flutter_windows.ps1"

endlocal

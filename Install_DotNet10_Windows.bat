@echo off
setlocal
set "ROOT=%~dp0"

powershell.exe -NoExit -ExecutionPolicy Bypass -File "%ROOT%scripts\install_dotnet10_windows.ps1"

endlocal

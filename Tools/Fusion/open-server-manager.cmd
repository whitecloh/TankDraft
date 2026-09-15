@echo off
setlocal
rem Do not pass PowerShell 7 module paths into Windows PowerShell 5.1.
set "PSModulePath="
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0open-server-manager.ps1"
if errorlevel 1 pause
endlocal

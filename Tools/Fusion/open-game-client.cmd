@echo off
setlocal
rem Do not pass PowerShell 7 module paths into Windows PowerShell 5.1.
set "PSModulePath="
set "helper=%~dp0open-game-client.ps1"
if not exist "%helper%" set "helper=%~dp0..\..\..\Tools\Fusion\open-game-client.ps1"
if not exist "%helper%" (
  echo Owner QA launcher helper is missing. This launcher is not portable; run it from the TankDraft workspace.
  pause
  exit /b 1
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%helper%"
)
set "result=%errorlevel%"
if not "%result%"=="0" pause
endlocal & exit /b %result%

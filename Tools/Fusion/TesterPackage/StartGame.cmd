@echo off
setlocal
set "PSModulePath="
set "launcher=%~dp0Launch.ps1"
if not exist "%launcher%" (
  echo Tester launcher is missing.
  pause
  exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%launcher%" %*
set "result=%errorlevel%"
if not "%result%"=="0" pause
endlocal & exit /b %result%

@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer\Install.ps1"
set "result=%errorlevel%"
pause
exit /b %result%

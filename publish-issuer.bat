@echo off
setlocal
if not "%~1"=="" exit /b 2
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\Publish-Issuer.ps1"
exit /b %errorlevel%

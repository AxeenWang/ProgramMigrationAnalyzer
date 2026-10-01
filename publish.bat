@echo off
setlocal
if not "%~2"=="" exit /b 2
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\Publish-Client.ps1" -PublicKeyPath "%~1"
exit /b %errorlevel%

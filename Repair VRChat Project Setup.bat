@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-Source.ps1" -Action repair %*
if errorlevel 1 pause
exit /b %errorlevel%

@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-Source.ps1" -Action install %*
pause
exit /b %errorlevel%

@echo off
setlocal
if /i not "%~1"=="--no-pause" (
	echo This removes VRChat Project Setup and its user PATH entry.
	pause
)

where pwsh >nul 2>nul
if errorlevel 1 (
	powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-VrcSetup.ps1" -InstallRoot "%~dp0." -DeferredCleanup
	exit /b %errorlevel%
) else (
	pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-VrcSetup.ps1" -InstallRoot "%~dp0." -DeferredCleanup
	exit /b %errorlevel%
)

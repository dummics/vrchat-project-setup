@echo off
setlocal
where pwsh >nul 2>nul
if %errorlevel%==0 (
	set "PS_EXE=pwsh"
) else (
	set "PS_EXE=powershell"
)

%PS_EXE% -NoProfile -ExecutionPolicy Bypass -File "%~dp0Repair-VrcSetup.ps1" -InstallRoot "%~dp0."
set "RESULT=%errorlevel%"
if /i not "%~1"=="--no-pause" pause
exit /b %RESULT%

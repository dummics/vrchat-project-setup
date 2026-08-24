@echo off
setlocal
where pwsh >nul 2>nul
if %errorlevel%==0 (
	set "PS_EXE=pwsh"
) else (
	set "PS_EXE=powershell"
)

if exist "%~dp0.vrcsetup-installed" (
	set "TARGET_ROOT=%~dp0."
) else if defined VRCSETUP_INSTALL_ROOT (
	set "TARGET_ROOT=%VRCSETUP_INSTALL_ROOT%"
) else (
	set "TARGET_ROOT=%LOCALAPPDATA%\Programs\VrcSetup"
)

if not exist "%TARGET_ROOT%\.vrcsetup-installed" (
	echo VRChat Project Setup is not installed for this user.
	echo Run INSTALL.bat, or use START VRCHAT SETUP.bat in portable mode.
	if /i not "%~1"=="--no-pause" pause
	exit /b 2
)

%PS_EXE% -NoProfile -ExecutionPolicy Bypass -File "%TARGET_ROOT%\Repair-VrcSetup.ps1" -InstallRoot "%TARGET_ROOT%"
set "RESULT=%errorlevel%"
if /i not "%~1"=="--no-pause" pause
exit /b %RESULT%

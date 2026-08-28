@echo off
setlocal
set "NO_PAUSE=0"
set "CONFIRM_UNINSTALL=1"

:parseArgs
if "%~1"=="" goto argsParsed
if /i "%~1"=="--no-pause" set "NO_PAUSE=1"
if /i "%~1"=="--yes" set "CONFIRM_UNINSTALL=0"
shift
goto parseArgs

:argsParsed
if exist "%~dp0.vrcsetup-installed" (
	set "TARGET_ROOT=%~dp0."
) else if defined VRCSETUP_INSTALL_ROOT (
	set "TARGET_ROOT=%VRCSETUP_INSTALL_ROOT%"
) else (
	set "TARGET_ROOT=%LOCALAPPDATA%\Programs\VrcSetup"
)

if not exist "%TARGET_ROOT%\.vrcsetup-installed" (
	echo VRChat Project Setup is not installed for this user.
	echo Nothing was removed. The downloaded folder is safe.
	if "%NO_PAUSE%"=="0" pause
	exit /b 2
)

if "%CONFIRM_UNINSTALL%"=="1" (
	echo This removes VRChat Project Setup, its terminal alias, and Windows shortcuts.
	choice /C YN /N /M "Continue? [Y/N]: "
	if errorlevel 2 (
		echo Uninstall cancelled. Nothing was removed.
		exit /b 0
	)
)

where pwsh >nul 2>nul
if errorlevel 1 (
	powershell -NoProfile -ExecutionPolicy Bypass -File "%TARGET_ROOT%\setup-scripts\maintenance\Uninstall-VrcSetup.ps1" -InstallRoot "%TARGET_ROOT%" -DeferredCleanup
	exit /b
) else (
	pwsh -NoProfile -ExecutionPolicy Bypass -File "%TARGET_ROOT%\setup-scripts\maintenance\Uninstall-VrcSetup.ps1" -InstallRoot "%TARGET_ROOT%" -DeferredCleanup
	exit /b
)

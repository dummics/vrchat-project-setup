@echo off
setlocal
set "NO_PAUSE=0"
set "NO_LAUNCH=0"

:parse_args
if /i "%~1"=="--no-pause" (
	set "NO_PAUSE=1"
	shift
	goto parse_args
)
if /i "%~1"=="--no-launch" (
	set "NO_LAUNCH=1"
	shift
	goto parse_args
)

where pwsh >nul 2>nul
if %errorlevel%==0 (
	set "PS_EXE=pwsh"
) else (
	set "PS_EXE=powershell"
)

if defined VRCSETUP_INSTALL_ROOT (
	set "TARGET_ROOT=%VRCSETUP_INSTALL_ROOT%"
) else (
	set "TARGET_ROOT=%LOCALAPPDATA%\Programs\VrcSetup"
)

%PS_EXE% -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-scripts\maintenance\Install-VrcSetup.ps1" -InstallRoot "%TARGET_ROOT%"
set "RESULT=%errorlevel%"
echo.
if "%RESULT%"=="0" (
	echo Installation complete.
	echo Search Windows for: VRChat Project Setup
	echo Terminal command: vrcsetup
	if "%NO_PAUSE%"=="0" (
		echo.
		echo Press any key to open VRChat Project Setup now.
		pause >nul
	)
	if "%NO_LAUNCH%"=="0" call "%TARGET_ROOT%\VRChat Project Setup.bat"
) else (
	echo Installation failed with exit code %RESULT%.
	if "%NO_PAUSE%"=="0" pause
)
exit /b %RESULT%

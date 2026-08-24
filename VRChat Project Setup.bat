@echo off
setlocal

set "LOCAL_ROOT=%~dp0"
if exist "%LOCAL_ROOT%.vrcsetup-installed" (
	call "%LOCAL_ROOT%setup-scripts\setup.bat" %*
	exit /b
)

if defined VRCSETUP_INSTALL_ROOT (
	set "INSTALLED_ROOT=%VRCSETUP_INSTALL_ROOT%"
) else (
	set "INSTALLED_ROOT=%LOCALAPPDATA%\Programs\VrcSetup"
)

if exist "%INSTALLED_ROOT%\.vrcsetup-installed" (
	call "%INSTALLED_ROOT%\setup-scripts\setup.bat" %*
	exit /b
)

call "%LOCAL_ROOT%setup-scripts\setup.bat" %*
exit /b

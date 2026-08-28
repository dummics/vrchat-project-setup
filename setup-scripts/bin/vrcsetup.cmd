@echo off
if /i "%~1"=="repair" (
	shift
	call "%~dp0..\..\Repair VRChat Project Setup.bat" --no-pause %*
	exit /b
)
if /i "%~1"=="uninstall" (
	shift
	call "%~dp0..\..\Uninstall VRChat Project Setup.bat" --no-pause %*
	exit /b
)
call "%~dp0..\setup.bat" %*
exit /b

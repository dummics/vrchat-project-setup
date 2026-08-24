@echo off
if /i "%~1"=="repair" (
	call "%~dp0..\REPAIR.bat" --no-pause
	exit /b %errorlevel%
)
if /i "%~1"=="uninstall" (
	call "%~dp0..\UNINSTALL.bat" --no-pause
	exit /b %errorlevel%
)
call "%~dp0..\setup-scripts\setup.bat" %*
exit /b %errorlevel%

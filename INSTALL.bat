@echo off
setlocal
where pwsh >nul 2>nul
if %errorlevel%==0 (
	set "PS_EXE=pwsh"
) else (
	set "PS_EXE=powershell"
)

%PS_EXE% -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-VrcSetup.ps1"
set "RESULT=%errorlevel%"
echo.
if "%RESULT%"=="0" (
	echo Installation complete. Open a new terminal and type: vrcsetup
) else (
	echo Installation failed with exit code %RESULT%.
)
pause
exit /b %RESULT%

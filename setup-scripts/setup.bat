@echo off
:: VRChat Setup Wizard - Wrapper batch (uses main unified entrypoint)
setlocal

:: Prefer PowerShell 7 (pwsh) if available, fallback to Windows PowerShell.
where pwsh >nul 2>nul
if %errorlevel%==0 (
	set "PS_EXE=pwsh"
) else (
	set "PS_EXE=powershell"
)

:: No arguments opens the wizard; arguments are forwarded to the PowerShell CLI.
if "%~1"=="" (
	%PS_EXE% -NoProfile -ExecutionPolicy Bypass -File "%~dp0vrc-setup-script.ps1" -Wizard
) else (
	%PS_EXE% -NoProfile -ExecutionPolicy Bypass -File "%~dp0vrc-setup-script.ps1" %*
)
exit /b %errorlevel%

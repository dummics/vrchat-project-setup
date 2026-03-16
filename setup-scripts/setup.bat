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

:: Run the wizard in the CURRENT terminal session.
%PS_EXE% -NoProfile -ExecutionPolicy Bypass -File "%~dp0vrc-setup-script.ps1" -Wizard
exit /b %errorlevel%

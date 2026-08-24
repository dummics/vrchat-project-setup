@echo off
:: VRChat Setup Wizard - Shortcut
:: Delegate with an absolute path without changing the caller's working directory.
call "%~dp0setup-scripts\setup.bat" %*
exit /b %errorlevel%

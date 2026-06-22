@echo off
setlocal
title Mangrove - Install service

rem Re-launch this script elevated (UAC prompt) if we're not already an administrator.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"

echo ============================================
echo  Installing the Mangrove Windows service
echo ============================================
echo.

"%~dp0Mangrove.exe" install
set EXITCODE=%errorlevel%

echo.
if %EXITCODE% neq 0 (
    echo Install failed (exit code %EXITCODE%).
    echo If the service already exists, run Uninstall-Mangrove.bat first.
) else (
    echo Done. You can close this window.
)
echo.
pause
endlocal

@echo off
setlocal
title Mangrove - Uninstall service

rem Re-launch this script elevated (UAC prompt) if we're not already an administrator.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"

echo ============================================
echo  Removing the Mangrove Windows service
echo ============================================
echo.

"%~dp0Mangrove.exe" uninstall

echo.
echo Done. You can close this window.
echo.
pause
endlocal

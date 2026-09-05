@echo off
rem Elite Dangerous plugin for Stream Dock (Ajazz) - uninstaller.
rem Elevates to administrator (needed to patch Stream Dock in Program Files), then runs Uninstall.ps1.

setlocal
set "HERE=%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
    exit /b
)

echo Uninstalling the Elite Dangerous plugin and profile bridge for Stream Dock...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%Uninstall.ps1"
set RC=%errorlevel%

echo.
if %RC% neq 0 (
    echo Uninstallation FAILED with code %RC%.
) else (
    echo Uninstallation complete.
)
echo.
pause

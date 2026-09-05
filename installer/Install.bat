@echo off
rem Elite Dangerous plugin for Stream Dock (Ajazz) - installer.
rem Elevates to administrator (needed to patch Stream Dock in Program Files), then runs Install.ps1.

setlocal
set "HERE=%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
    exit /b
)

echo Installing the Elite Dangerous plugin and profile bridge for Stream Dock...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%Install.ps1"
set RC=%errorlevel%

echo.
if %RC% neq 0 (
    echo Installation FAILED with code %RC%.
) else (
    echo Installation complete.
)
echo.
pause

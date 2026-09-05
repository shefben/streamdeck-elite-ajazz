@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
cd /d "%~dp0.."
cl /nologo /LD /O2 /GS- /EHa /Fe:build\streamdock-profile-bridge.dll /Fo:build\ src\bridge.cpp /link /DLL kernel32.lib

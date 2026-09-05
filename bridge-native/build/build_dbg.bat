@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
cd /d "%~dp0.."
cl /nologo /LD /O2 /GS- /EHa /DBRIDGE_DEBUG /Fe:build\bridge-dbg.dll /Fo:build\dbg_ src\bridge.cpp /link /DLL kernel32.lib user32.lib

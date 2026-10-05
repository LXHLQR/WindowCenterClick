@echo off
setlocal
cd /d "%~dp0"
if exist "WindowCenterClick.exe" if exist "build-version-2.txt" goto run
call "%~dp0Build.cmd"
if errorlevel 1 exit /b 1
:run
start "" "%~dp0WindowCenterClick.exe"

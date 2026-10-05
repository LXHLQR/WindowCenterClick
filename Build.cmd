@echo off
setlocal
cd /d "%~dp0"
set "CENTER_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CENTER_CSC%" set "CENTER_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CENTER_CSC%" (
    echo C# compiler not found. Install or repair Microsoft .NET Framework 4.8.
    pause
    exit /b 1
)
echo Building WindowCenterClick...
"%CENTER_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"WindowCenterClick.exe" /win32manifest:"app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "WindowCenterClick.cs"
if errorlevel 1 (
    echo Build failed. Close the running app, extract all ZIP files, and try again.
    pause
    exit /b 1
)
echo Build complete: WindowCenterClick.exe
> "build-version-2.txt" echo 2
exit /b 0

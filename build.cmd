@echo off
rem Builds CMDPlus.exe with the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Couldn't find csc.exe. .NET Framework 4.x is required.
    exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /win32icon:CMDPlus.ico /resource:CMDPlus.ico,CMDPlus.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:CMDPlus.exe CMDPlus.cs
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)
echo Built CMDPlus.exe

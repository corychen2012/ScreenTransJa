@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] .NET Framework 4.x compiler not found: %CSC%
    echo         Please install .NET Framework 4.8 first.
    exit /b 1
)

if not exist dist mkdir dist

"%CSC%" /nologo /warn:0 /codepage:65001 /target:winexe /win32icon:app.ico /out:dist\ScreenTransJa.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Core.dll src\Config.cs src\Translator.cs src\OverlayForm.cs src\SelectionFrame.cs src\ResultForm.cs src\SettingsForm.cs src\ChangeDetector.cs src\Pricing.cs src\Program.cs

if errorlevel 1 (
    echo [ERROR] build failed
    exit /b 1
)

echo.
echo [OK] dist\ScreenTransJa.exe

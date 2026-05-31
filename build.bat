@echo off
setlocal
cd /d "%~dp0"
echo Baue GabelstaplerKameraMonitor...

where msbuild >nul 2>nul
if %errorlevel%==0 (
    msbuild GabelstaplerKameraMonitor.sln /p:Configuration=Release /p:Platform="Any CPU"
    goto :end
)

if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" GabelstaplerKameraMonitor.sln /p:Configuration=Release /p:Platform="Any CPU"
    goto :end
)

if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" (
    "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" GabelstaplerKameraMonitor.sln /p:Configuration=Release /p:Platform="Any CPU"
    goto :end
)

echo MSBuild wurde nicht gefunden. Bitte in Visual Studio oeffnen und Build ^> Build Solution ausfuehren.

:end
pause

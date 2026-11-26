@echo off
setlocal

set DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe
if not exist "%DOTNET%" set DOTNET=dotnet

echo Building AutoSwitching...
"%DOTNET%" build "%~dp0src\AutoSwitching\AutoSwitching.csproj" -c Release
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Done: src\AutoSwitching\bin\Release\net48\AutoSwitching.exe
echo This single exe runs on Windows 10 1903+ / Windows 11 with no runtime install.
pause

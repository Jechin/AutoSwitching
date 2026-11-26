@echo off
setlocal

set DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe
if not exist "%DOTNET%" set DOTNET=dotnet

echo Building AutoSwitching...
"%DOTNET%" build "%~dp0AutoSwitching.sln" -c Release
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Done: src\AutoSwitching\bin\Release\net8.0-windows\AutoSwitching.exe
pause

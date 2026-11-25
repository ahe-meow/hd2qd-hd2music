@echo off
rem Checks, then builds release\HD2QD.HD2Music.dll, a plugin of HD2 Mod Quick Deploy (added on
rem its Plugins page). Needs the .NET SDK (8 or later): dotnet on PATH, or set DOTNET to its
rem dotnet.exe. Verify.exe checks the contract, the text, the icon, the framework references and
rem builds both windows off-screen; it needs no game, network or manager.
cd /d "%~dp0"
if "%DOTNET%"=="" set DOTNET=dotnet
"%DOTNET%" build src\HD2QD.HD2Music.csproj -c Release || exit /b 1
if not exist release mkdir release
copy /y "src\bin\Release\net48\HD2QD.HD2Music.dll" "release\HD2QD.HD2Music.dll" >nul || exit /b 1
"%DOTNET%" build tests\Verify.csproj -c Release || exit /b 1
"tests\bin\Release\net48\Verify.exe" "release\HD2QD.HD2Music.dll" || exit /b 1
echo.
echo Built: release\HD2QD.HD2Music.dll

@echo off
rem Builds two flavours of HD2QD.HD2Music.dll, a plugin of HD2 Mod Quick Deploy:
rem   release\HD2QD.HD2Music.dll       full, FFmpeg inside (~21 MB)
rem   release\HD2QD.HD2Music-Lite.dll lite, uses an existing ffmpeg.exe (~340 KB)
rem Needs the .NET SDK (8 or later): dotnet on PATH, or set DOTNET to its dotnet.exe.
rem Verify.exe checks each file's contract, text, icon, framework references, the tab path and
rem builds the view and windows off-screen; it needs no game, network or manager.
cd /d "%~dp0"
if "%DOTNET%"=="" set DOTNET=dotnet
if not exist release mkdir release
if not exist release\full mkdir release\full
if not exist release\lite mkdir release\lite

echo Building full (embedded FFmpeg)...
"%DOTNET%" build src\HD2QD.HD2Music.csproj -c Release -p:EmbeddedFfmpeg=true -p:BaseIntermediateOutputPath=obj\full\ -o release\full || exit /b 1
echo Building lite (external FFmpeg)...
"%DOTNET%" build src\HD2QD.HD2Music.csproj -c Release -p:EmbeddedFfmpeg=false -p:BaseIntermediateOutputPath=obj\lite\ -o release\lite || exit /b 1

copy /y "release\full\HD2QD.HD2Music.dll" "release\HD2QD.HD2Music.dll" >nul || exit /b 1
copy /y "release\lite\HD2QD.HD2Music.dll" "release\HD2QD.HD2Music-Lite.dll" >nul || exit /b 1

"%DOTNET%" build tests\Verify.csproj -c Release || exit /b 1
"tests\bin\Release\net48\Verify.exe" "release\HD2QD.HD2Music.dll" || exit /b 1
echo.
"tests\bin\Release\net48\Verify.exe" "release\HD2QD.HD2Music-Lite.dll" || exit /b 1

echo.
echo Built: release\HD2QD.HD2Music.dll       (full, embedded FFmpeg)
echo Built: release\HD2QD.HD2Music-Lite.dll (lite, external FFmpeg)

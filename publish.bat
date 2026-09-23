@echo off
setlocal

cd /d "%~dp0"
if errorlevel 1 exit /b 1

set "MSBUILDDISABLENODEREUSE=1"
set "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1"
set "PUBLISH_DIR=%~dp0publish\win-x64-single-file"
set "EXE=%PUBLISH_DIR%\ProgramMigrationAnalyzer.App.exe"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET 10 SDK is required on the build computer.
    exit /b 1
)

dotnet publish "src\ProgramMigrationAnalyzer.App\ProgramMigrationAnalyzer.App.csproj" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%PUBLISH_DIR%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:PublishTrimmed=false ^
    -p:PublishDocumentationFiles=false ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -p:UseSharedCompilation=false ^
    -m:1 -nr:false

if errorlevel 1 (
    echo Publish failed.
    exit /b 1
)

if not exist "%EXE%" (
    echo Publish finished, but the expected EXE was not found: "%EXE%"
    exit /b 1
)

echo.
echo Ready to share: "%EXE%"
echo The recipient does not need to install .NET.
echo HTML preview requires the Microsoft Edge WebView2 Runtime on the recipient's PC.
exit /b 0

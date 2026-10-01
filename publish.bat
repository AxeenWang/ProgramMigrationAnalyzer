@echo off
setlocal

if exist "%~dp0src\ProgramMigrationAnalyzer.App\LoginWindow.xaml" goto publishCurrentCheckout

rem Preserve the legacy checkout and delegate to its current login worktree.
set "SOURCE_ROOT=%~dp0.codex-tmp\2026-10-01_login-phase-1\worktree"
set "SOURCE_PUBLISH=%SOURCE_ROOT%\publish.bat"
set "SOURCE_EXE=%SOURCE_ROOT%\publish\win-x64-single-file\ProgramMigrationAnalyzer.App.exe"
set "PUBLISH_DIR=%~dp0publish\win-x64-single-file"
set "EXE=%PUBLISH_DIR%\ProgramMigrationAnalyzer.App.exe"

if not exist "%SOURCE_PUBLISH%" (
    echo Publish source is unavailable: "%SOURCE_ROOT%"
    echo Restore the current worktree before publishing.
    exit /b 1
)

if not exist "%SOURCE_ROOT%\src\ProgramMigrationAnalyzer.App\LoginWindow.xaml" (
    echo Publish source does not contain the login implementation: "%SOURCE_ROOT%"
    exit /b 1
)

echo Build source: "%SOURCE_ROOT%"
call "%SOURCE_PUBLISH%"
if errorlevel 1 exit /b 1

if not exist "%SOURCE_EXE%" (
    echo Publish finished, but the source EXE was not found: "%SOURCE_EXE%"
    exit /b 1
)

if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"
if errorlevel 1 exit /b 1

copy /y "%SOURCE_EXE%" "%EXE%" >nul
if errorlevel 1 (
    echo Cannot update the published EXE. Close the running EXE and try again.
    exit /b 1
)

echo.
echo Ready to share from this directory: "%EXE%"
echo Local login is included. Each recipient PC requires local account setup.
exit /b 0

:publishCurrentCheckout
cd /d "%~dp0"
if errorlevel 1 exit /b 1

set "MSBUILDDISABLENODEREUSE=1"
set "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1"
set "PUBLISH_DIR=%~dp0publish\win-x64-single-file"
set "EXE=%PUBLISH_DIR%\ProgramMigrationAnalyzer.App.exe"

echo Build source: "%~dp0"
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
echo Local login is included. Each recipient PC requires local account setup.
exit /b 0
